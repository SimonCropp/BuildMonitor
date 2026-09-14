/// <summary>
/// The loop for one connection. Each cycle discovers pipelines when due, groups them by what one
/// fetch covers, asks <see cref="PollSchedule"/> which groups are due, fetches those, and applies
/// the result. It never throws out; every failure becomes a health on the connection or on the
/// group it happened to.
/// </summary>
sealed class ConnectionPoller
{
    string connectionId;
    SessionHost host;
    ISecretStore secrets;
    DurationHistory history;
    HttpMessageHandler handler;
    TokenRefresher? refresher;
    Func<DateTimeOffset> clock;
    Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    CancelSource stop = new();
    HashSet<string> recorded = [];
    ETagCache etags = new();
    DateTimeOffset rotated = DateTimeOffset.MinValue;
    RateBudget budget;
    RequestBucket? bucket;
    long spentRequests;
    double spentCost;
    Task? loop;
    ImmutableArray<Pipeline> discoveredPipelines = [];
    DateTimeOffset discovered = DateTimeOffset.MinValue;
    // The option the pipelines above were discovered under. Changing it rediscovers at once, or
    // hidden repositories would linger, or shown ones stay missing, until the next rediscovery.
    bool? discoveredWithForks;
    int discoveryFailures;
    ImmutableDictionary<string, GroupMemory> memory = ImmutableDictionary<string, GroupMemory>.Empty;
    // Nudges and refreshes arrive from other threads, and a cycle in flight must not lose them.
    ConcurrentDictionary<string, byte> nudges = new();
    int refreshRequested;
    DateTimeOffset? pausedUntil;
    DateTimeOffset? wakeAt;
    int failures;
    int rateLimits;
    DateTimeOffset probed = DateTimeOffset.MinValue;
    int probeFailures;
    bool probeWorks = true;
    bool probedOnce;

    /// <summary>
    /// How often the pipeline list is re-read. New pipelines are rare and discovery is the
    /// expensive half of a poll on the providers that need a call per repository.
    /// </summary>
    public static readonly TimeSpan RediscoverAfter = TimeSpan.FromMinutes(10);

    public const int PerPipeline = 5;

    public ConnectionPoller(string connectionId, SessionHost host, ISecretStore secrets, DurationHistory history, HttpMessageHandler handler, TokenRefresher? refresher, Func<DateTimeOffset>? clock = null)
    {
        this.connectionId = connectionId;
        this.host = host;
        this.secrets = secrets;
        this.history = history;
        this.handler = handler;
        this.refresher = refresher;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        budget = new(this.clock);
    }

    /// <summary>
    /// When the next cycle is due, as of the last one.
    /// </summary>
    public DateTimeOffset? WakeAt => wakeAt;

    public void Start(Cancel cancel)
    {
        var linked = CancelSource.CreateLinkedTokenSource(cancel, stop.Token);
        loop = Task.Run(() => Run(linked.Token), Cancel.None);
    }

    /// <summary>
    /// Every group is due on the next cycle. A rate limit pause still has to end first.
    /// </summary>
    public void Refresh()
    {
        Interlocked.Exchange(ref refreshRequested, 1);
        Wake();
    }

    /// <summary>
    /// The pipeline's group is due on the next cycle, and stays on the poll interval for a while,
    /// as after a retry, a cancel, or a push.
    /// </summary>
    public void Nudge(string pipelineId)
    {
        nudges[pipelineId] = 0;
        Wake();
    }

    /// <summary>
    /// Plans again without making anything due, as after a settings change.
    /// </summary>
    public void Wake() =>
        wake.Writer.TryWrite(true);

    public void Stop() =>
        stop.Cancel();

    public Task WaitForExit() =>
        loop ?? Task.CompletedTask;

    /// <summary>
    /// A refresh and one cycle, whatever the schedule or a pause says.
    /// </summary>
    public Task<ConnectionHealth> PollOnce(Cancel cancel)
    {
        Interlocked.Exchange(ref refreshRequested, 1);
        return Cycle(true, cancel);
    }

    /// <summary>
    /// One scheduled cycle: only what is due, and nothing during a pause.
    /// </summary>
    public Task<ConnectionHealth> PollDue(Cancel cancel) =>
        Cycle(false, cancel);

    async Task Run(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            await PollDue(cancel);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await WaitFor(Delay(), cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// How long to sleep: out a pause, whatever arrived; until a wake alone when sign in is
    /// required, because nothing changes about a dead token on its own; not at all when a refresh
    /// or nudge is waiting; otherwise until the schedule's next due group.
    /// </summary>
    TimeSpan? Delay()
    {
        var now = clock();
        if (Paused(now) is { } remaining)
        {
            return remaining;
        }

        if (host.State.Connection(connectionId)?.Health == ConnectionHealth.NeedsAuth &&
            Volatile.Read(ref refreshRequested) == 0)
        {
            return null;
        }

        if (Volatile.Read(ref refreshRequested) == 1 ||
            !nudges.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        return wakeAt is { } at ? Max(at - now, TimeSpan.Zero) : RediscoverAfter;
    }

    async Task WaitFor(TimeSpan? delay, Cancel cancel)
    {
        // A wake that arrived during the cycle has already been planned for.
        while (wake.Reader.TryRead(out _))
        {
        }

        if (delay is null)
        {
            await wake.Reader.ReadAsync(cancel);
            return;
        }

        if (delay.Value <= TimeSpan.Zero)
        {
            return;
        }

        using var timeout = CancelSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(delay.Value);
        try
        {
            await wake.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // The delay elapsed.
        }
    }

    async Task<ConnectionHealth> Cycle(bool ignorePause, Cancel cancel)
    {
        var connection = host.State.Connection(connectionId)?.Connection;
        if (connection is null)
        {
            return ConnectionHealth.Error;
        }

        if (!ignorePause &&
            Paused(clock()) is not null)
        {
            return Health();
        }

        var provider = Providers.Get(connection.ProviderId);
        var secret = secrets.Read(SecretKeys.Token(connection.Id));
        if (secret is null)
        {
            Set(ConnectionHealth.NeedsAuth, "No credential stored");
            return ConnectionHealth.NeedsAuth;
        }

        var everything = Interlocked.Exchange(ref refreshRequested, 0) == 1;
        // Only a first poll or a refresh shows progress; a scheduled cycle every few seconds would
        // make the header flicker.
        var visible = everything || Health() == ConnectionHealth.Unpolled;
        if (visible)
        {
            host.Mutate(_ => MonitorSession.SetHealth(_, connectionId, ConnectionHealth.Polling));
        }

        try
        {
            return await Fetch(provider, connection, secret, everything, visible, ignorePause, cancel);
        }
        catch (AuthException exception)
        {
            if (refresher is not null &&
                await refresher.TryRefresh(connection, cancel))
            {
                try
                {
                    return await Fetch(provider, connection, secrets.Read(SecretKeys.Token(connection.Id))!, everything, visible, ignorePause, cancel);
                }
                catch (AuthException again)
                {
                    exception = again;
                }
            }

            wakeAt = null;
            Set(ConnectionHealth.NeedsAuth, exception.Message);
            return ConnectionHealth.NeedsAuth;
        }
        catch (RateLimitException exception)
        {
            Limited(exception);
            return ConnectionHealth.RateLimited;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return ConnectionHealth.Error;
        }
        catch (Exception exception)
        {
            failures++;
            Log.Warning(exception, "Polling {Connection} failed", connection.Name);
            wakeAt = clock() + Backoff.Next(Interval(host.State.Settings), failures);
            Set(ConnectionHealth.Error, exception.Message);
            return ConnectionHealth.Error;
        }
    }

    async Task<ConnectionHealth> Fetch(IProvider provider, Connection connection, string secret, bool everything, bool visible, bool ignorePause, Cancel cancel)
    {
        var context = Providers.Context(connection, secret, handler, etags, budget) with
        {
            Progress = visible ? progress => host.Mutate(_ => MonitorSession.SetProgress(_, connectionId, progress)) : _ => { },
            ShowForksAndCollaborations = host.State.Settings.ShowForksAndCollaborations
        };
        var descriptor = provider.Descriptor;
        var now = clock();
        Rotate(descriptor, now);

        var rediscovered = await Discover(provider, context, connection, now, cancel);
        var state = host.State;
        var pipelines = discoveredPipelines.Where(_ => !Filters.ExcludesPipeline(state.Settings.Filters, _)).ToImmutableArray();
        var groups = PollGroup.Of(descriptor.FetchUnit, pipelines);
        Remember(descriptor, groups, now);
        // The probe and each group's own requests report no progress: the header counts groups.
        var quiet = context with { Progress = _ => { } };
        await Probe(provider, quiet, descriptor, groups, rediscovered, now, cancel);
        var plan = PollSchedule.Plan(Input(descriptor, groups, state, everything, ignorePause, now));
        bucket = plan.Bucket;
        var halted = 0;
        var results = await Concurrently.Settle(
            plan.Fetch,
            descriptor.FetchConcurrency,
            async (group, token) =>
            {
                // After a rate limit or a refused token the rest would only be refused too.
                if (Volatile.Read(ref halted) == 1)
                {
                    return null;
                }

                try
                {
                    return await provider.FetchBuilds(quiet, group.Pipelines, PerPipeline, token);
                }
                catch (Exception exception) when (exception is RateLimitException or AuthException { Status: HttpStatusCode.Unauthorized })
                {
                    Interlocked.Exchange(ref halted, 1);
                    throw;
                }
            },
            cancel,
            visible ? context.Progress : null);

        var fetched = ImmutableHashSet.CreateBuilder<string>();
        var firstFetch = ImmutableHashSet.CreateBuilder<string>();
        var builds = new List<Build>();
        RateLimitException? rateLimit = null;
        AuthException? unauthorized = null;
        var attempted = 0;
        var forbidden = 0;
        for (var index = 0; index < plan.Fetch.Length; index++)
        {
            var group = plan.Fetch[index];
            var (value, exception) = results[index];
            if (value is null &&
                exception is null)
            {
                continue;
            }

            attempted++;
            var previous = memory.GetValueOrDefault(group.Key) ?? GroupMemory.New;
            switch (exception)
            {
                case null:
                    foreach (var pipeline in group.Pipelines)
                    {
                        fetched.Add(pipeline.Id);
                        if (!previous.FetchedPipelines.Contains(pipeline.Id))
                        {
                            firstFetch.Add(pipeline.Id);
                        }
                    }

                    builds.AddRange(value!);
                    memory = memory.SetItem(group.Key, previous with
                    {
                        LastAttempt = now,
                        Failures = 0,
                        Error = null,
                        FetchedPipelines = previous.FetchedPipelines.Union(group.Pipelines.Select(_ => _.Id))
                    });
                    break;
                case RateLimitException limit:
                    rateLimit ??= limit;
                    break;
                case AuthException { Status: HttpStatusCode.Unauthorized } refused:
                    unauthorized ??= refused;
                    break;
                default:
                    if (exception is AuthException)
                    {
                        forbidden++;
                    }

                    Log.Warning(exception, "Fetching {Group} of {Connection} failed", group.Key, connection.Name);
                    memory = memory.SetItem(group.Key, previous with
                    {
                        LastAttempt = now,
                        Failures = previous.Failures + 1,
                        Error = exception.Message
                    });
                    break;
            }
        }

        RecordDurations(builds);
        failures = 0;
        var (health, error, retryAfter) = await Health(connection, groups, rateLimit, unauthorized, attempted, forbidden, fetched.Count, cancel);
        var outcome = new FetchOutcome(pipelines, fetched.ToImmutable(), firstFetch.ToImmutable(), [..builds], health, error, retryAfter);
        var current = host.State.Connection(connectionId);
        if (fetched.Count > 0 ||
            rediscovered ||
            visible ||
            current?.Health != health ||
            current.Error != error)
        {
            var medians = history.Medians();
            host.Mutate(_ => MonitorSession.ApplyMedians(MonitorSession.ApplyFetch(_, connectionId, outcome, clock()), medians));
        }

        Spend(descriptor);
        var after = clock();
        var next = PollSchedule.Plan(Input(descriptor, groups, host.State, false, false, after)).WakeAt;
        var rediscover = discovered + RediscoverAfter;
        wakeAt = health switch
        {
            ConnectionHealth.NeedsAuth => null,
            ConnectionHealth.RateLimited => retryAfter,
            _ when unauthorized is not null => after,
            _ => Earliest(Earliest(rediscover, next), NextProbe(descriptor))
        };

        if (plan.Fetch.Length > 0)
        {
            Log.Information(
                "{Connection}: fetched {Attempted} of {Groups} groups, {Deferred} deferred, {Requests} requests sent, {Remaining} of the limit left",
                connection.Name,
                attempted,
                groups.Length,
                plan.Deferred.Length,
                budget.SentCount,
                budget.State.Remaining);
            Log.Debug("{Connection} schedule\n{Plan}", connection.Name, PollSchedule.Describe(plan, now));
        }

        return health;
    }

    /// <summary>
    /// Asks the provider what changed, and nudges the groups whose token moved. It runs at most
    /// every probe interval, and not in a cycle that just rediscovered, which read the same thing.
    /// The first probe only records. A probe that fails only puts off the next probe, because the
    /// schedule still fetches every group in time; a rate limit or a refused token is the
    /// connection's problem and goes up.
    /// </summary>
    async Task Probe(IProvider provider, ProviderContext context, ProviderDescriptor descriptor, ImmutableArray<PollGroup> groups, bool rediscovered, DateTimeOffset now, Cancel cancel)
    {
        if (NextProbe(descriptor) is not { } due ||
            due > now)
        {
            return;
        }

        probed = now;
        if (rediscovered)
        {
            // Discovery has just read the same listing, so this counts as the probe's turn. Left
            // unset, the first probe would be due in the year 1 and every wake time with it.
            return;
        }
        var previous = memory
            .Where(_ => _.Value.SeenActivity is not null)
            .ToImmutableDictionary(_ => _.Key, _ => _.Value.SeenActivity!);
        ImmutableDictionary<string, string>? activity;
        try
        {
            activity = await provider.RecentActivity(context, groups, previous, cancel);
            probeFailures = 0;
        }
        catch (Exception exception) when (exception is not (RateLimitException or AuthException { Status: HttpStatusCode.Unauthorized } or OperationCanceledException))
        {
            probeFailures++;
            Log.Warning(exception, "Probing {Connection} for activity failed", context.Connection.Name);
            return;
        }

        if (activity is null)
        {
            probeWorks = false;
            return;
        }

        foreach (var (key, token) in activity)
        {
            if (!memory.TryGetValue(key, out var group) ||
                group.SeenActivity == token)
            {
                continue;
            }

            var news = probedOnce && group.LastAttempt is not null;
            memory = memory.SetItem(key, group with
            {
                SeenActivity = token,
                NudgedAt = news ? now : group.NudgedAt
            });
        }

        probedOnce = true;
    }

    DateTimeOffset? NextProbe(ProviderDescriptor descriptor) =>
        probeWorks
            ? probed + Backoff.Next(descriptor.ProbeInterval ?? Interval(host.State.Settings), probeFailures)
            : null;

    static DateTimeOffset Earliest(DateTimeOffset at, DateTimeOffset? other) =>
        other is { } candidate && candidate < at ? candidate : at;

    /// <summary>
    /// Re-reads the pipeline list when due. A failure keeps the list already known and tries again
    /// after a backoff; only with nothing known yet does it fail the cycle.
    /// </summary>
    async Task<bool> Discover(IProvider provider, ProviderContext context, Connection connection, DateTimeOffset now, Cancel cancel)
    {
        var due = discoveredPipelines.Length == 0 ||
                  now - discovered > RediscoverAfter ||
                  discoveredWithForks != context.ShowForksAndCollaborations;
        var inDebt = bucket is { Tokens: < 0 };
        if (!due ||
            (inDebt && discoveredPipelines.Length > 0))
        {
            return false;
        }

        try
        {
            discoveredPipelines = [..await provider.DiscoverPipelines(context, cancel)];
            discovered = now;
            discoveredWithForks = context.ShowForksAndCollaborations;
            discoveryFailures = 0;
            return true;
        }
        catch (Exception exception) when (discoveredPipelines.Length > 0 &&
                                          exception is not (AuthException or RateLimitException or OperationCanceledException))
        {
            discoveryFailures++;
            discovered = now - RediscoverAfter + Backoff.Next(TimeSpan.FromMinutes(1), discoveryFailures);
            Log.Warning(exception, "Discovering {Connection} failed; keeping the pipelines already known", connection.Name);
            return false;
        }
    }

    /// <summary>
    /// Keeps memory for the groups that exist now, and stamps the groups of nudged pipelines.
    /// </summary>
    void Remember(ProviderDescriptor descriptor, ImmutableArray<PollGroup> groups, DateTimeOffset now)
    {
        var next = ImmutableDictionary.CreateBuilder<string, GroupMemory>();
        foreach (var group in groups)
        {
            next[group.Key] = memory.GetValueOrDefault(group.Key) ?? GroupMemory.New;
        }

        foreach (var pipelineId in nudges.Keys)
        {
            nudges.TryRemove(pipelineId, out _);
            var pipeline = groups.SelectMany(_ => _.Pipelines).FirstOrDefault(_ => _.Id == pipelineId);
            if (pipeline is null)
            {
                continue;
            }

            var key = PollGroup.KeyOf(descriptor.FetchUnit, pipeline);
            next[key] = next[key] with { NudgedAt = now };
        }

        memory = next.ToImmutable();
    }

    ScheduleInput Input(ProviderDescriptor descriptor, ImmutableArray<PollGroup> groups, SessionState state, bool everything, bool ignorePause, DateTimeOffset now)
    {
        var settings = state.Settings;
        var interval = Interval(settings);
        return new(
            connectionId,
            descriptor.Quota,
            descriptor.IdleCap,
            groups,
            memory,
            Filters.Apply(settings.Filters, state.Builds.Where(_ => _.ConnectionId == connectionId)),
            state.Medians,
            interval,
            Min(TimeSpan.FromSeconds(Math.Max(5, settings.RunningPollIntervalSeconds)), interval),
            budget.State,
            ignorePause ? null : PausedUntil(),
            bucket,
            everything,
            now);
    }

    async Task<(ConnectionHealth Health, string? Error, DateTimeOffset? RetryAfter)> Health(
        Connection connection,
        ImmutableArray<PollGroup> groups,
        RateLimitException? rateLimit,
        AuthException? unauthorized,
        int attempted,
        int forbidden,
        int fetched,
        Cancel cancel)
    {
        if (rateLimit is not null)
        {
            return (ConnectionHealth.RateLimited, rateLimit.Message, Limited(rateLimit, apply: false));
        }

        if (fetched > 0)
        {
            rateLimits = 0;
        }

        if (unauthorized is not null &&
            !(refresher is not null && await refresher.TryRefresh(connection, cancel)))
        {
            return (ConnectionHealth.NeedsAuth, unauthorized.Message, null);
        }

        var failing = groups
            .Select(_ => memory.GetValueOrDefault(_.Key))
            .Where(_ => _ is { Failures: > 0 })
            .Select(_ => _!.Error)
            .ToList();
        if (attempted > 0 &&
            forbidden == attempted &&
            fetched == 0 &&
            failing.Count == groups.Length)
        {
            return (ConnectionHealth.NeedsAuth, failing[0], null);
        }

        if (failing.Count == 0)
        {
            return (ConnectionHealth.Ok, null, null);
        }

        var error = groups.Length == 1 ? failing[0] : $"{failing.Count} of {groups.Length} failed: {failing[0]}";
        return (ConnectionHealth.Error, error, null);
    }

    /// <summary>
    /// A service that names no time gets a minute, doubling while it keeps refusing, as GitHub
    /// asks of a client that hits its secondary limits.
    /// </summary>
    DateTimeOffset Limited(RateLimitException exception, bool apply = true)
    {
        rateLimits++;
        var until = clock() + (exception.RetryAfter ?? Backoff.Next(TimeSpan.FromMinutes(1), rateLimits));
        pausedUntil = until;
        wakeAt = until;
        if (apply)
        {
            Set(ConnectionHealth.RateLimited, exception.Message, until);
        }

        return until;
    }

    /// <summary>
    /// Charges the request bucket what the cycle actually cost, discovery included.
    /// </summary>
    void Spend(ProviderDescriptor descriptor)
    {
        var sent = budget.SentCount;
        var cost = budget.CostTotal;
        if (descriptor.Quota is { } quota &&
            bucket is { } current)
        {
            bucket = current.Spend(quota.ChargeByCost ? cost - spentCost : sent - spentRequests);
        }

        spentRequests = sent;
        spentCost = cost;
    }

    /// <summary>
    /// ETag entries used to be dropped at each discovery unless requested since the one before.
    /// A group in backoff, under rate pressure or on a long idle cap can go longer than that, and
    /// losing its ETag turns a free 304 into a counted 200 exactly when the budget is short. So the
    /// cache rotates on the clock, slower than the longest interval a group can have.
    /// </summary>
    void Rotate(ProviderDescriptor descriptor, DateTimeOffset now)
    {
        if (rotated == DateTimeOffset.MinValue)
        {
            rotated = now;
            return;
        }

        var longest = Max(Backoff.Max, descriptor.IdleCap ?? PollSchedule.DefaultIdleCap);
        var retention = Max(TimeSpan.FromMinutes(30), longest * 3);
        if (now - rotated >= retention)
        {
            etags.Rotate();
            rotated = now;
        }
    }

    DateTimeOffset? PausedUntil()
    {
        var asked = budget.State.PausedUntil;
        return pausedUntil is { } limited && (asked is null || limited > asked) ? limited : asked;
    }

    TimeSpan? Paused(DateTimeOffset now) =>
        PausedUntil() is { } until && until > now ? until - now : null;

    ConnectionHealth Health() =>
        host.State.Connection(connectionId)?.Health ?? ConnectionHealth.Error;

    static TimeSpan Interval(Settings settings) =>
        TimeSpan.FromSeconds(Math.Max(5, settings.PollIntervalSeconds));

    /// <summary>
    /// Every finished successful run is recorded once. Its own timestamps say how long it took;
    /// no state about what was running last time is needed.
    /// </summary>
    void RecordDurations(IReadOnlyList<Build> builds)
    {
        foreach (var build in builds)
        {
            if (build.Status != BuildStatus.Succeeded ||
                build.Started is null ||
                build.Finished is null)
            {
                continue;
            }

            var key = $"{build.PipelineKey}/{build.RunNumber}/{build.ProviderRef}";
            if (recorded.Add(key))
            {
                history.Record(build.PipelineKey, build.Finished.Value - build.Started.Value);
            }
        }
    }

    void Set(ConnectionHealth health, string? error, DateTimeOffset? retryAfter = null) =>
        host.Mutate(_ => MonitorSession.SetHealth(_, connectionId, health, error, retryAfter));

    static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left > right ? left : right;

    static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left < right ? left : right;
}
