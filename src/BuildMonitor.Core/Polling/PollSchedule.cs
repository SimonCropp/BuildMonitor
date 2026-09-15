/// <summary>
/// How often each group of a connection is fetched.
/// <para>
/// Every pipeline used to be fetched on one clock, and one queued or running build anywhere put
/// the whole connection on the running interval. A GitHub connection of three hundred
/// repositories then sent about a thousand requests a minute, over GitHub's secondary limit even
/// when every answer was a 304, while most of those repositories had not built in days.
/// </para>
/// <para>
/// Now a group follows its busiest pipeline. A running build is fetched on the running interval
/// while it is expected to finish, from the fastest to the slowest of its pipeline's recent runs,
/// and is due the moment that window opens; before it, and for a queued build, the poll interval
/// applies, and past it the interval grows with the overrun. A quiet pipeline slows with the time
/// since its last build, a thirtieth of it, or a hundred and twentieth after a failure, until the
/// idle cap. On top of that come failure backoff, pressure from a draining quota, and a request
/// quota that defers the least urgent groups.
/// </para>
/// </summary>
static class PollSchedule
{
    public static readonly TimeSpan DefaultIdleCap = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A queued or running build older than this counts as quiet: a queue stuck waiting for a
    /// runner that never comes would otherwise hold its group on the fast interval for days.
    /// </summary>
    static TimeSpan staleActive = TimeSpan.FromHours(6);

    /// <summary>
    /// How long a nudged group stays on the poll interval. A push shows up before its run exists,
    /// so one fetch straight after the nudge can find nothing new.
    /// </summary>
    static TimeSpan nudgeWindow = TimeSpan.FromMinutes(3);

    static TimeSpan minimumGap = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How near the end of a provider's countdown a build counts as finishing, and how far past the
    /// slowest run it still does: a run a little slower than any before is not yet a hung one.
    /// </summary>
    static TimeSpan finishingMargin = TimeSpan.FromSeconds(90);

    public static TimeSpan IdleCap(ScheduleInput input) =>
        Max(input.IdleCap ?? DefaultIdleCap, input.Interval);

    public static TimeSpan MaximumInterval(ScheduleInput input) =>
        Max(Max(Backoff.Max, input.Interval), IdleCap(input));

    public static SchedulePlan Plan(ScheduleInput input)
    {
        var now = input.Now;
        var byPipeline = input.Builds.ToLookup(_ => _.PipelineId);
        var plans = ImmutableArray.CreateBuilder<GroupPlan>(input.Groups.Length);
        foreach (var group in input.Groups)
        {
            plans.Add(Group(input, group, byPipeline));
        }

        var quota = Effective(input);
        var bucket = quota is null ? input.Bucket : (input.Bucket ?? RequestBucket.Full(quota, now)).Refill(quota, now);
        var window = Min(TimeSpan.FromSeconds(5), input.RunningInterval / 4);
        var due = new List<int>();
        if (!(input.PausedUntil > now))
        {
            for (var index = 0; index < plans.Count; index++)
            {
                if (plans[index].DueAt <= now + window)
                {
                    due.Add(index);
                }
            }
        }

        var ordered = due
            .OrderBy(_ => plans[_].Reason)
            .ThenBy(_ => plans[_].DueAt)
            .ThenBy(_ => _)
            .ToList();
        var allowance = quota is not null && bucket is { } available
            ? (int) Math.Floor(Math.Max(0, available.Tokens))
            : int.MaxValue;
        var fetch = ImmutableArray.CreateBuilder<PollGroup>();
        var deferred = ImmutableArray.CreateBuilder<string>();
        foreach (var index in ordered)
        {
            if (fetch.Count < allowance)
            {
                fetch.Add(input.Groups[index]);
            }
            else
            {
                deferred.Add(input.Groups[index].Key);
            }
        }

        var planned = due.ToHashSet();
        DateTimeOffset? wake = null;
        for (var index = 0; index < plans.Count; index++)
        {
            if (!planned.Contains(index))
            {
                wake = Earliest(wake, plans[index].DueAt);
            }
        }

        if (deferred.Count > 0 &&
            quota is not null &&
            bucket is { } refilled)
        {
            wake = Earliest(wake, refilled.Spend(fetch.Count).NextToken(quota));
        }

        if (wake is { } at)
        {
            wake = Later(Max(at, now + minimumGap), input.PausedUntil);
        }
        else if (input.PausedUntil > now)
        {
            wake = input.PausedUntil;
        }

        return new(plans.ToImmutable(), fetch.ToImmutable(), deferred.ToImmutable(), wake, bucket);
    }

    public static GroupPlan Group(ScheduleInput input, PollGroup group) =>
        Group(input, group, input.Builds.ToLookup(_ => _.PipelineId));

    static GroupPlan Group(ScheduleInput input, PollGroup group, ILookup<string, Build> byPipeline)
    {
        var now = input.Now;
        var memory = input.Memory.GetValueOrDefault(group.Key) ?? GroupMemory.New;
        if (memory.LastAttempt is not { } lastAttempt)
        {
            return new(group.Key, ScheduleReason.Unfetched, TimeSpan.Zero, Later(now, input.PausedUntil));
        }

        var (reason, interval) = GroupInterval(input, group, byPipeline);
        // A group that has never fetched has no builds to judge by; read as quiet, a failure on its
        // first fetch would leave its rows empty for the whole idle cap. It retries on backoff.
        if (memory is
            {
                Failures: > 0,
                FetchedPipelines.IsEmpty: true
            })
        {
            (reason, interval) = (ScheduleReason.Backoff, Backoff.Next(input.Interval, memory.Failures));
        }

        if (memory.NudgedAt is { } nudged &&
            now - nudged < nudgeWindow &&
            interval > input.Interval)
        {
            (reason, interval) = (ScheduleReason.Nudged, input.Interval);
        }

        if (memory.Failures > 0 &&
            Backoff.Next(input.Interval, memory.Failures) is var backoff &&
            backoff > interval)
        {
            (reason, interval) = (ScheduleReason.Backoff, backoff);
        }

        var pressure = Pressure(input.Rate, now);
        if (pressure > 1)
        {
            interval = Max(interval, Min(interval * pressure, MaximumInterval(input)));
        }

        // A pipeline the group has never fetched, such as a workflow added since the last fetch.
        var fresh = memory.Failures == 0 &&
                    group.Pipelines.Any(_ => !memory.FetchedPipelines.Contains(_.Id));
        var dueAt = input.Everything || fresh || memory.NudgedAt > lastAttempt
            ? now
            : lastAttempt + interval * (1 + Spread(input.ConnectionId, group.Key));
        // Sooner when a running build's finish window opens before the next tick, but never to cut
        // short a backoff or a draining quota, which stretched the interval on purpose.
        if (memory.Failures == 0 &&
            pressure <= 1 &&
            WindowOpens(input, group, byPipeline) is { } opens &&
            opens < dueAt)
        {
            dueAt = opens;
        }

        var later = Later(dueAt, input.PausedUntil);
        return new(group.Key, fresh ? ScheduleReason.Unfetched : reason, interval, later);
    }

    static (ScheduleReason Reason, TimeSpan Interval) GroupInterval(ScheduleInput input, PollGroup group, ILookup<string, Build> byPipeline)
    {
        (ScheduleReason Reason, TimeSpan Interval)? best = null;
        foreach (var pipeline in group.Pipelines)
        {
            var candidate = PipelineInterval(input, byPipeline[pipeline.Id]);
            if (best is not { } current ||
                candidate.Interval < current.Interval ||
                (candidate.Interval == current.Interval && candidate.Reason < current.Reason))
            {
                best = candidate;
            }
        }

        return best ?? (ScheduleReason.Quiet, IdleCap(input));
    }

    public static (ScheduleReason Reason, TimeSpan Interval) PipelineInterval(ScheduleInput input, IEnumerable<Build> builds)
    {
        var now = input.Now;
        var list = builds.ToList();
        (ScheduleReason Reason, TimeSpan Interval)? active = null;
        foreach (var build in list)
        {
            if (!build.IsActive ||
                now - (build.Started ?? build.Queued ?? now) > staleActive)
            {
                continue;
            }

            (ScheduleReason Reason, TimeSpan Interval) candidate = build.Status == BuildStatus.Queued
                ? (ScheduleReason.Queued, input.Interval)
                : Running(input, build);
            if (active is not { } current ||
                candidate.Interval < current.Interval ||
                (candidate.Interval == current.Interval && candidate.Reason < current.Reason))
            {
                active = candidate;
            }
        }

        if (active is { } found)
        {
            return found;
        }

        var cap = IdleCap(input);
        var latest = list.MaxBy(_ => _.Ordering ?? DateTimeOffset.MinValue);
        var last = list.Select(Activity).Max();
        if (latest is null ||
            last is null)
        {
            return (ScheduleReason.Quiet, cap);
        }

        var failed = latest.Status == BuildStatus.Failed;
        var age = Max(TimeSpan.Zero, now - last.Value);
        var interval = Max(input.Interval, Min(age / (failed ? 120 : 30), cap));
        if (interval >= cap)
        {
            return (ScheduleReason.Quiet, cap);
        }

        return (failed ? ScheduleReason.RecentFailure : ScheduleReason.RecentSuccess, interval);
    }

    /// <summary>
    /// A running build is fetched on the running interval only while it is expected to finish:
    /// before that the countdown needs no fresh data, and a long build fetched every ten seconds for
    /// its whole run cost several times the requests for the same news. Past that its estimate was
    /// plainly wrong, so the interval grows with the overrun, a tenth of it: a hung build was
    /// otherwise fetched every ten seconds until it went stale hours later.
    /// </summary>
    static (ScheduleReason Reason, TimeSpan Interval) Running(ScheduleInput input, Build build)
    {
        if (Position(build, input.Durations, input.Now) is not { } position)
        {
            // Nothing to estimate against, so any moment could be the end.
            return (ScheduleReason.Finishing, input.RunningInterval);
        }

        if (position.PastClose > TimeSpan.Zero)
        {
            return (ScheduleReason.Overrun, Max(input.Interval, Min(position.PastClose / 10, IdleCap(input))));
        }

        if (position.UntilOpen > TimeSpan.Zero)
        {
            return (ScheduleReason.Running, input.Interval);
        }

        return (ScheduleReason.Finishing, input.RunningInterval);
    }

    /// <summary>
    /// Where a running build stands against the window it is expected to finish in: how long until
    /// the window opens, and how far past its close the build is, each negative until it happens.
    /// A provider's countdown opens it within the margin of the end and closes it the margin past.
    /// A countdown at exactly zero may be one that stops there rather than going negative, which
    /// says nothing of how far past the end a build is, so then the elapsed time is measured instead
    /// against the window of the provider's duration or the history, where there is one: from its
    /// fastest run until the margin past its slowest.
    /// </summary>
    static (TimeSpan UntilOpen, TimeSpan PastClose)? Position(Build build, ImmutableDictionary<string, DurationRange> durations, DateTimeOffset now)
    {
        var window = Estimator.Window(build, durations);
        if (build.Estimate?.Remaining is { } remaining &&
            (remaining != TimeSpan.Zero || window is null))
        {
            return (remaining - finishingMargin, -remaining - finishingMargin);
        }

        if (window is not { } range)
        {
            return null;
        }

        var elapsed = now - (build.Started ?? build.Queued ?? now);
        return (range.Fastest - elapsed, elapsed - range.Slowest - finishingMargin);
    }

    /// <summary>
    /// When the soonest finish window of the group's running builds opens, if one is still to open,
    /// so the group is due then rather than on its next tick, which could start the running interval
    /// most of a poll interval late. Not for a build with a provider's countdown: that is as of the
    /// last fetch, so it names no fixed moment, and the next fetch reads a fresher one anyway.
    /// </summary>
    static DateTimeOffset? WindowOpens(ScheduleInput input, PollGroup group, ILookup<string, Build> byPipeline)
    {
        DateTimeOffset? soonest = null;
        foreach (var build in group.Pipelines.SelectMany(_ => byPipeline[_.Id]))
        {
            if (build.Status != BuildStatus.Running ||
                build.Started is not { } started ||
                build.Estimate?.Remaining is not null ||
                input.Now - started > staleActive ||
                Estimator.Window(build, input.Durations) is not { } window)
            {
                continue;
            }

            var opens = started + window.Fastest;
            if (opens > input.Now)
            {
                soonest = Earliest(soonest, opens);
            }
        }

        return soonest;
    }

    static DateTimeOffset? Activity(Build build)
    {
        DateTimeOffset? latest = null;
        foreach (var at in new[] { build.Queued, build.Started, build.Finished })
        {
            if (at is { } value &&
                (latest is null || value > latest))
            {
                latest = value;
            }
        }

        return latest;
    }

    /// <summary>
    /// How much a draining quota stretches intervals: not at all above a quarter left, and up to
    /// eight times as it runs out, unless the window resets within a minute anyway.
    /// </summary>
    public static double Pressure(RateState rate, DateTimeOffset now)
    {
        if (!rate.Current(now))
        {
            return 1;
        }

        var pressure = 1d;
        if (rate is { Remaining: { } remaining, Limit: > 0 and var limit } &&
            (rate.Reset is not { } reset || reset - now > TimeSpan.FromMinutes(1)))
        {
            var fraction = remaining / limit;
            if (fraction < 0.25)
            {
                pressure = Math.Clamp(0.25 / Math.Max(fraction, 1d / 32), 1, 8);
            }
        }

        if (rate.NearLimit)
        {
            return Math.Max(pressure, 4);
        }

        return pressure;
    }

    /// <summary>
    /// Up to a tenth either side of the interval, fixed per group. Three hundred groups discovered
    /// together would otherwise fall due in the same second, every time. string.GetHashCode is
    /// randomised per process, so the hash is FNV-1a.
    /// </summary>
    public static double Spread(string connectionId, string key)
    {
        var hash = 2166136261u;
        foreach (var character in $"{connectionId}/{key}")
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        return hash % 20001 / 100000d - 0.1;
    }

    /// <summary>
    /// A text table of a plan: for the debug log, and for snapshot tests.
    /// </summary>
    public static string Describe(SchedulePlan plan, DateTimeOffset now)
    {
        var fetched = plan.Fetch.Select(_ => _.Key).ToHashSet();
        var builder = new StringBuilder();
        foreach (var group in plan.Groups)
        {
            var action = fetched.Contains(group.Key) ? "fetch" : plan.Deferred.Contains(group.Key) ? "defer" : "wait";
            var key = group.Key.Length == 0 ? "(connection)" : group.Key;
            builder.AppendLine($"{key,-32} {group.Reason,-13} {Span(group.Interval),7} {Offset(group.DueAt - now),8} {action}");
        }

        builder.Append($"wake {(plan.WakeAt is { } wake ? Offset(wake - now) : "on demand")}");
        return builder.ToString();
    }

    static RequestQuota? Effective(ScheduleInput input)
    {
        if (input is
            {
                Quota:
                {
                    LearnLimit: true
                } quota,
                Rate.Limit: > 0 and var limit
            })
        {
            return quota with
            {
                Requests = limit
            };
        }

        return input.Quota;
    }

    static string Span(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return "0s";
        }

        if (span.TotalHours >= 1)
        {
            if (span.Minutes == 0)
            {
                return $"{(int) span.TotalHours}h";
            }

            return $"{(int) span.TotalHours}h{span.Minutes}m";
        }

        if (span.TotalMinutes >= 1)
        {
            if (span.Seconds == 0)
            {
                return $"{(int) span.TotalMinutes}m";
            }

            return $"{(int) span.TotalMinutes}m{span.Seconds}s";
        }

        return $"{(int) Math.Round(span.TotalSeconds)}s";
    }

    static string Offset(TimeSpan offset)
    {
        if (offset <= TimeSpan.Zero)
        {
            return "now";
        }

        return $"+{Span(offset)}";
    }

    static DateTimeOffset Later(DateTimeOffset at, DateTimeOffset? paused)
    {
        if (paused is { } until && until > at)
        {
            return until;
        }

        return at;
    }

    static DateTimeOffset Earliest(DateTimeOffset? current, DateTimeOffset at)
    {
        if (current is { } known && known <= at)
        {
            return known;
        }

        return at;
    }

    static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left > right ? left : right;

    static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left > right ? left : right;

    static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left < right ? left : right;
}
