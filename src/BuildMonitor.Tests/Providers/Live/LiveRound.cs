/// <summary>
/// One round of retry and cancel against a provider's sandbox: every action the app offers on a
/// row, against the real service. The round leaves the sandbox as the next one needs it, with
/// its newest build failed and retryable.
/// </summary>
sealed class LiveRound(LiveConnection live, ProviderContext context, Pipeline sandbox, IReadOnlyList<Pipeline> group, Cancel cancel)
{
    /// <summary>
    /// Providers whose sandbox runs one build at a time, so a second start while one runs waits in
    /// the queue. Only that reaches their queue cancels reliably: Jenkins cancels a queue item
    /// through its own endpoint, and TeamCity cancels a queued build through the build queue.
    /// A queued build caught by chance may start between the read and the cancel.
    /// </summary>
    static ImmutableHashSet<string> queueing = ["jenkins", "teamcity"];

    static TimeSpan settle = TimeSpan.FromMinutes(5);
    static TimeSpan run = TimeSpan.FromMinutes(10);
    static TimeSpan drain = TimeSpan.FromMinutes(10);

    // Bitbucket counts every request against an hourly budget, GitHub per minute.
    TimeSpan interval = TimeSpan.FromSeconds(live.Descriptor.Quota is null ? 5 : 10);

    string Id => live.Id;

    /// <summary>
    /// Asks access first and discovers with the answer, as the poller does. Octopus narrows cancel
    /// per deployment from the grants that access remembers.
    /// </summary>
    public static async Task Run(LiveConnection live, Cancel cancel)
    {
        var memory = new ProviderMemory();
        var cache = new ETagCache();
        var access = await live.Provider.Access(live.Context(memory, cache), cancel);
        if (access == BuildAccess.Watch)
        {
            throw new InvalidOperationException($"{live.Id}: the token can only watch builds. Retrying and cancelling need {live.Descriptor.ActionPermission ?? "write access"}.");
        }

        var context = live.Context(memory, cache, access: access);
        var pipelines = await live.Provider.DiscoverPipelines(context, cancel);
        var (sandbox, problem) = LiveSandbox.Find(live, pipelines);
        if (sandbox is null)
        {
            throw new InvalidOperationException(problem);
        }

        LiveLog.Line($"{live.Id}: sandbox {live.Pipeline} found among {pipelines.Count} pipelines, access {access}");
        var group = LiveSandbox.Group(live, pipelines, sandbox);
        await new LiveRound(live, context, sandbox, group, cancel).Steps();
    }

    async Task Steps()
    {
        // Whatever an earlier round left running is stopped first. Otherwise its cancel or its
        // failure would be taken for this round's.
        var idle = await Drain();

        await LiveStarter.Start(live, context, sandbox, idle, skipWhenNone: true, cancel);

        // Running rather than queued. A queued build can start between the read and the cancel,
        // and is then cancelled through a queue path that no longer applies. GoCD offers no cancel
        // before a build runs anyway.
        var started = await Until("a running build that can be cancelled", _ => _.Any(Running), LiveSettings.QueueTimeout);
        if (queueing.Contains(Id))
        {
            started = await CancelQueued(started);
        }

        await Cancel(started.First(Running));
        var cancelled = await Until("the cancel to land", _ => Settled(_, BuildStatus.Cancelled), settle);

        // Starting from the cancelled build tests retrying one, and the run's failure leaves the
        // sandbox ready for the next round.
        await LiveStarter.Start(live, context, sandbox, cancelled, skipWhenNone: false, cancel);
        await Until("the new run to start", _ => _.Any(_ => _.IsActive), LiveSettings.QueueTimeout);
        var failed = await Until("the new run to fail", _ => Settled(_, BuildStatus.Failed), run);
        var last = failed[0];
        if (Id != "octopus")
        {
            await Assert.That(last.Retryable()).IsTrue().Because($"{Id}: the sandbox's newest build must offer a retry, or the next round has nothing to start from");
        }

        // The marker proves the log fetched belongs to this run.
        var log = await live.Provider.FetchLog(context, last, cancel);
        var marked = log.Contains(LiveSettings.Marker, StringComparison.Ordinal);
        LiveLog.Line($"{Id}: the log of {LiveLog.Row(last)} is {log.Length} characters, marker {(marked ? "found" : "missing")}");
        await Assert.That(marked).IsTrue().Because($"{Id}: the failed run's log should contain '{LiveSettings.Marker}'");
    }

    /// <summary>
    /// Starts a second run behind the running one, and cancels it while it waits.
    /// </summary>
    async Task<IReadOnlyList<Build>> CancelQueued(IReadOnlyList<Build> builds)
    {
        await LiveStarter.Start(live, context, sandbox, builds, skipWhenNone: false, cancel);
        var waiting = await Until("a build queued behind the running one", _ => _.Any(Queued), TimeSpan.FromMinutes(2));
        await Cancel(waiting.First(Queued));
        return await Until(
            "the queued build to leave the queue while the first still runs",
            _ => !_.Any(Queued) &&
                 _.Any(Running),
            settle);
    }

    /// <summary>
    /// Reads until nothing is active, asking once to cancel each cancellable build. A build that
    /// cannot be cancelled, such as an Octopus deployment outside the key's grants or a queued
    /// GoCD run, is waited out instead.
    /// </summary>
    Task<IReadOnlyList<Build>> Drain()
    {
        var asked = new HashSet<string>();
        return LivePoll.Until(
            Id,
            "the sandbox to be idle",
            async _ =>
            {
                var builds = await Read(_);
                foreach (var build in builds.Where(_ => _ is { IsActive: true, CanCancel: true }))
                {
                    if (!asked.Add($"{build.RunNumber}|{build.Status}"))
                    {
                        continue;
                    }

                    try
                    {
                        await Cancel(build);
                    }
                    catch (HttpRequestException exception)
                    {
                        LiveLog.Line($"{Id}: cancelling a leftover failed with {exception.StatusCode}. It may have just finished.");
                    }
                }

                return builds;
            },
            _ => !_.Any(_ => _.IsActive),
            drain,
            interval,
            cancel);
    }

    async Task Cancel(Build build)
    {
        LiveLog.Line($"{Id}: cancelling {LiveLog.Row(build)}");
        await live.Provider.Cancel(context, build, cancel);
    }

    /// <summary>
    /// The sandbox's builds, newest first, fetched with the rest of its poll group as the app fetches them.
    /// </summary>
    async Task<IReadOnlyList<Build>> Read(Cancel token)
    {
        var builds = await live.Provider.FetchBuilds(context, group, ConnectionPoller.PerPipeline, token);
        return LiveSandbox.Newest(builds.Where(_ => _.PipelineId == sandbox.Id));
    }

    Task<IReadOnlyList<Build>> Until(string awaited, Func<IReadOnlyList<Build>, bool> done, TimeSpan timeout) =>
        LivePoll.Until(Id, awaited, Read, done, timeout, interval, cancel);

    static bool Running(Build build) =>
        build is
        {
            Status: BuildStatus.Running,
            CanCancel: true
        };

    static bool Queued(Build build) =>
        build is
        {
            Status: BuildStatus.Queued,
            CanCancel: true
        };

    /// <summary>
    /// Nothing is active, and the newest build ended as <paramref name="status"/>.
    /// </summary>
    static bool Settled(IReadOnlyList<Build> builds, BuildStatus status) =>
        builds.Count > 0 &&
        !builds.Any(_ => _.IsActive) &&
        builds[0].Status == status;
}
