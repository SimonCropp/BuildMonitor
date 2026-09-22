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
    ProviderMemory providerMemory = new();
    IdentityNames identities;
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
    // The credential the pipelines were discovered with. Another one, as after the user pastes a new
    // token, may see other pipelines and have other rights, so it is discovered with at once.
    string? discoveredWithSecret;
    // What the credential may do to builds, as asked before the last discovery.
    BuildAccess access;
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
    // The fetch cycles since the last summary line, and the requests sent before them.
    DateTimeOffset? summarized;
    int summaryCycles;
    int summaryGroups;
    long summarySent;

    /// <summary>
    /// How often the information log summarises a connection's fetch cycles.
    /// </summary>
    public static readonly TimeSpan SummaryEvery = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often the pipeline list is re-read. New pipelines are rare and discovery is the
    /// expensive half of a poll on the providers that need a call per repository.
    /// </summary>
    public static readonly TimeSpan RediscoverAfter = TimeSpan.FromMinutes(10);

    public const int PerPipeline = 5;

    public ConnectionPoller(string connectionId, SessionHost host, ISecretStore secrets, DurationHistory history, HttpMessageHandler handler, TokenRefresher? refresher, Func<DateTimeOffset>? clock = null, IdentityNames? identities = null)
    {
        this.connectionId = connectionId;
        this.host = host;
        this.secrets = secrets;
        this.history = history;
        this.handler = handler;
        this.refresher = refresher;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.identities = identities ?? new();
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
                await WaitFor(cancel);
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

        if (wakeAt is { } at)
        {
            return Max(at - now, TimeSpan.Zero);
        }

        return RediscoverAfter;
    }

    /// <summary>
    /// Sleeps out <see cref="Delay"/>, or until woken. A wake that arrived during the cycle has
    /// already been planned for, so it is drained first, and the delay is read only after. Read
    /// before, a refresh landing between the two had its flag missed by the delay and its wake
    /// drained, and waited out the whole schedule instead of polling at once.
    /// </summary>
    async Task WaitFor(Cancel cancel)
    {
        while (wake.Reader.TryRead(out _))
        {
        }

        // Refresh and Nudge set their flag before they wake, so one arriving after the drain is
        // either seen here or left in the channel to end the sleep.
        var delay = Delay();
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
            ShowForksAndCollaborations = host.State.Settings.ShowForksAndCollaborations,
            Filters = host.State.Settings.Filters,
            Since = HistoryCutoff.Of(clock(), host.State.Settings.HistoryDays),
            Memory = providerMemory,
            Identities = identities
        };
        var descriptor = provider.Descriptor;
        var now = clock();
        Rotate(descriptor, now);

        var (rediscovered, accessChanged) = await Discover(provider, context, connection, secret, now, cancel);
        context = context with
        {
            Access = access
        };
        // Every group, so no row keeps offering what the connection may no longer do, or keeps
        // hiding what it now may, until its group comes due.
        everything |= accessChanged;
        var state = host.State;
        var pipelines = discoveredPipelines.Where(_ => !Filters.ExcludesPipeline(state.Settings.Filters, _)).ToImmutableArray();
        var groups = PollGroup.Of(descriptor.FetchUnit, pipelines);
        Remember(descriptor, groups, now);
        // The probe and each group's own requests report no progress: the header counts groups.
        var quiet = context with
        {
            Progress = _ => { }
        };
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
        var outcome = new FetchOutcome(pipelines, fetched.ToImmutable(), firstFetch.ToImmutable(), [..builds], health, error, retryAfter, access);
        var current = host.State.Connection(connectionId);
        if (fetched.Count > 0 ||
            rediscovered ||
            visible ||
            current?.Health != health ||
            current.Error != error ||
            current.Access != access)
        {
            var medians = history.Medians();
            Settings? lifted = null;
            host.Mutate(_ =>
            {
                var next = MonitorSession.ApplyMedians(MonitorSession.ApplyFetch(_, connectionId, outcome, clock()), medians);
                if (next.Settings.Deferrals != _.Settings.Deferrals)
                {
                    lifted = next.Settings;
                }

                return next;
            });
            if (lifted is not null)
            {
                await SaveLifted(lifted);
            }
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
            LogCycle(connection, plan, attempted, groups.Length, after);
            // A line per group, and built before Log.Debug could check the level, so every cycle
            // formatted the whole table to throw it away.
            if (Log.IsEnabled(LogEventLevel.Debug))
            {
                Log.Debug("{Connection} schedule\n{Plan}", connection.Name, PollSchedule.Describe(plan, now));
            }
        }

        return health;
    }

    /// <summary>
    /// Each cycle goes to the debug log, and the information log gets a summary of them every
    /// <see cref="SummaryEvery"/>. A line a cycle was one every few seconds for a connection of many
    /// quiet groups, most fetching one or two, and buried the warnings in the log a user sends with
    /// an issue. The first cycle is summarised at once, and a cycle the quota deferred groups in is
    /// logged as it happens: a quota running short is worth seeing when it did.
    /// </summary>
    void LogCycle(Connection connection, SchedulePlan plan, int attempted, int groups, DateTimeOffset now)
    {
        var remaining = budget.State.Remaining;
        var sent = budget.SentCount;
        Log.Debug(
            "{Connection}: fetched {Attempted} of {Groups} groups, {Deferred} deferred, {Requests} requests sent, {Remaining} of the limit left",
            connection.Name,
            attempted,
            groups,
            plan.Deferred.Length,
            sent,
            remaining);
        if (plan.Deferred.Length > 0)
        {
            Log.Information(
                "{Connection}: the request quota deferred {Deferred} of {Groups} groups",
                connection.Name,
                plan.Deferred.Length,
                groups);
        }

        summaryCycles++;
        summaryGroups += attempted;
        if (summarized is { } since &&
            now - since < SummaryEvery)
        {
            return;
        }

        var cycles = summaryCycles == 1 ? "1 cycle" : $"{summaryCycles} cycles";
        var limit = remaining is { } left ? $", {left} of the limit left" : "";
        Log.Information(
            "{Connection}: fetched {Fetched} groups of {Groups} in {Cycles}, {Requests} requests{Limit}",
            connection.Name,
            summaryGroups,
            groups,
            cycles,
            sent - summarySent,
            limit);
        summarized = now;
        summaryCycles = 0;
        summaryGroups = 0;
        summarySent = sent;
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

    DateTimeOffset? NextProbe(ProviderDescriptor descriptor)
    {
        if (probeWorks)
        {
            return probed + Backoff.Next(descriptor.ProbeInterval ?? Interval(host.State.Settings), probeFailures);
        }

        return null;
    }

    static DateTimeOffset Earliest(DateTimeOffset at, DateTimeOffset? other)
    {
        if (other is { } candidate &&
            candidate < at)
        {
            return candidate;
        }

        return at;
    }

    /// <summary>
    /// Re-reads the pipeline list when due, or at once with another credential, after asking what
    /// the credential may do, which a provider's discovery can narrow per pipeline. A failure keeps
    /// the list already known and tries again after a backoff; only with nothing known yet does it
    /// fail the cycle. Says whether it discovered, and whether the answer about the credential changed.
    /// </summary>
    async Task<(bool Rediscovered, bool AccessChanged)> Discover(IProvider provider, ProviderContext context, Connection connection, string secret, DateTimeOffset now, Cancel cancel)
    {
        var due = discoveredPipelines.Length == 0 ||
                  now - discovered > RediscoverAfter ||
                  discoveredWithForks != context.ShowForksAndCollaborations ||
                  discoveredWithSecret != secret;
        var inDebt = bucket is { Tokens: < 0 };
        if (!due ||
            (inDebt && discoveredPipelines.Length > 0))
        {
            return (false, false);
        }

        var before = access;
        access = await Access(provider, context, connection, secret, cancel);
        var changed = access != before;
        if (changed)
        {
            Log.Information("{Connection} build access: {Access}", connection.Name, access);
        }

        // Set whether discovery succeeds or not, or a failing one would be retried every cycle
        // rather than after its backoff.
        discoveredWithSecret = secret;
        try
        {
            discoveredPipelines = [
                ..await provider.DiscoverPipelines(
                    context with
                    {
                        Access = access
                    },
                    cancel)];
            discovered = now;
            discoveredWithForks = context.ShowForksAndCollaborations;
            discoveryFailures = 0;
            return (true, changed);
        }
        catch (Exception exception) when (discoveredPipelines.Length > 0 &&
                                          exception is not (AuthException or RateLimitException or OperationCanceledException))
        {
            discoveryFailures++;
            discovered = now - RediscoverAfter + Backoff.Next(TimeSpan.FromMinutes(1), discoveryFailures);
            Log.Warning(exception, "Discovering {Connection} failed; keeping the pipelines already known", connection.Name);
            return (false, changed);
        }
    }

    /// <summary>
    /// Asks the provider what the credential may do. A failure keeps the answer already known for
    /// the same credential, as a flicker to unknown would offer for a cycle what the connection may
    /// not do, and drops one about another credential. A rate limit or a refused token is the
    /// connection's problem and goes up.
    /// </summary>
    async Task<BuildAccess> Access(IProvider provider, ProviderContext context, Connection connection, string secret, Cancel cancel)
    {
        try
        {
            return await provider.Access(context, cancel);
        }
        catch (Exception exception) when (exception is not (RateLimitException or AuthException { Status: HttpStatusCode.Unauthorized }) &&
                                          !cancel.IsCancellationRequested)
        {
            Log.Warning(exception, "Asking what {Connection} may do to builds failed", connection.Name);
            if (discoveredWithSecret == secret)
            {
                return access;
            }

            return BuildAccess.Unknown;
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
            next[key] = next[key] with
            {
                NudgedAt = now
            };
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
            history.Ranges(),
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
        if (pausedUntil is { } limited &&
            (asked is null || limited > asked))
        {
            return limited;
        }

        return asked;
    }

    TimeSpan? Paused(DateTimeOffset now)
    {
        if (PausedUntil() is { } until &&
            until > now)
        {
            return until - now;
        }

        return null;
    }

    ConnectionHealth Health() =>
        host.State.Connection(connectionId)?.Health ?? ConnectionHealth.Error;

    static TimeSpan Interval(Settings settings) =>
        TimeSpan.FromSeconds(Math.Max(5, settings.PollIntervalSeconds));

    /// <summary>
    /// Saves the deferrals a fetch ended. Only in memory, one ended by a fix came back with the next
    /// start and hid that pipeline's next break, which is news.
    /// </summary>
    static async Task SaveLifted(Settings settings)
    {
        try
        {
            await SettingsHelper.Write(settings);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Saving the deferrals a poll ended failed");
        }
    }

    /// <summary>
    /// Every finished successful run is recorded once. Its own timestamps say how long it took;
    /// no state about what was running last time is needed.
    /// </summary>
    void RecordDurations(IReadOnlyList<Build> builds)
    {
        var lookup = recorded.GetAlternateLookup<CharSpan>();
        foreach (var build in builds)
        {
            if (build.Status != BuildStatus.Succeeded ||
                build.Started is null ||
                build.Finished is null)
            {
                continue;
            }

            if (AddRecorded(lookup, build))
            {
                history.Record(build.PipelineKey, build.Finished.Value - build.Started.Value);
            }
        }
    }

    /// <summary>
    /// Whether the run is new to <see cref="recorded"/>, adding it if so. The key is written on the
    /// stack and becomes a string only when it is added: most runs were recorded by an earlier poll,
    /// and building the key to find that out cost every finished run two strings a poll.
    /// </summary>
    static bool AddRecorded(HashSet<string>.AlternateLookup<CharSpan> lookup, Build build)
    {
        var length = build.ConnectionId.Length + build.PipelineId.Length + build.RunNumber.Length + build.ProviderRef.Length + 3;
        var key = length <= 256 ? stackalloc char[length] : new char[length];
        key.TryWrite($"{build.ConnectionId}/{build.PipelineId}/{build.RunNumber}/{build.ProviderRef}", out _);
        return lookup.Add(key);
    }

    void Set(ConnectionHealth health, string? error, DateTimeOffset? retryAfter = null) =>
        host.Mutate(_ => MonitorSession.SetHealth(_, connectionId, health, error, retryAfter));

    static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        if (left > right)
        {
            return left;
        }

        return right;
    }

    static TimeSpan Min(TimeSpan left, TimeSpan right)
    {
        if (left < right)
        {
            return left;
        }

        return right;
    }
}
