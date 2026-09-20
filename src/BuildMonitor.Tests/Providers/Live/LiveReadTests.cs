/// <summary>
/// Runs each provider against its real service, reading only. The tests are explicit, so a normal
/// run never includes them. Each reads its credential from the environment or user secrets, so
/// the wire format is checked without any credential reaching the repository.
/// <para>
/// Settings are <c>BUILDMONITOR_{PROVIDER}_TOKEN</c>, plus <c>_SERVER</c>, <c>_USER</c> and
/// <c>_SCOPE_{ID}</c> where the provider needs them, and <c>_PIPELINE</c> to name a sandbox.
/// PROVIDER is the descriptor id upper cased with hyphens as underscores, so Azure DevOps reads
/// <c>BUILDMONITOR_AZURE_DEVOPS_TOKEN</c>. docs/live-tests.md lists them all.
/// </para>
/// </summary>
[Explicit]
public class LiveReadTests
{
    // GitLab and Octopus have no cheap call that says whether anything changed.
    static ImmutableHashSet<string> withoutActivity = ["gitlab", "octopus"];

    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task SignIn(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var memory = new ProviderMemory();
        var test = await live.Provider.Test(live.Context(memory), cancel);
        var access = await live.Provider.Access(live.Context(memory), cancel);
        LiveLog.Line($"{providerId}: signed in, access {access}, the connection test reports {test.Access}");
        LiveLog.Detail($"{providerId}: {test.Describe(live.Descriptor)}");
        await Assert.That(test.Ok).IsTrue();
        if (test.Access != BuildAccess.Unknown)
        {
            await Assert.That(access).IsEqualTo(test.Access).Because($"{providerId}: the connection editor and the poller would disagree about what the token may do");
        }

        if (live.ExpectedAccess is { } expected)
        {
            await Assert.That(access).IsEqualTo(expected).Because($"{providerId}: {LiveSettings.Prefix(providerId)}ACCESS says the token should have {expected}");
        }
    }

    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task Discovery(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var session = await LiveSessions.Get(live, cancel);
        var pipelines = session.Pipelines;
        var repositories = pipelines.Select(_ => _.RepoName).Distinct().Count();
        LiveLog.Line($"{providerId}: {pipelines.Count} pipelines in {repositories} repositories, access {session.Access}");
        foreach (var pipeline in pipelines.Take(20))
        {
            LiveLog.Detail($"  {pipeline.Name} [{pipeline.Id}] {pipeline.Url}");
        }

        await Assert.That(pipelines.Count).IsGreaterThan(0).Because($"{providerId}: the account should have at least the sandbox");
        var duplicates = pipelines.Count - pipelines.Select(_ => _.Id).Distinct().Count();
        await Assert.That(duplicates).IsEqualTo(0).Because($"{providerId}: two pipelines with one id share a row key");
        var relative = pipelines.Count(_ => !Absolute(_.Url));
        await Assert.That(relative).IsEqualTo(0).Because($"{providerId}: a pipeline link that is not an absolute http(s) URL opens nothing");
        if (live.Pipeline is null)
        {
            return;
        }

        if (session.Sandbox is null)
        {
            Assert.Fail(session.SandboxProblem!);
            return;
        }

        LiveLog.Line($"{providerId}: sandbox {live.Pipeline} found");
    }

    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task Fetch(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var session = await LiveSessions.Get(live, cancel);
        var groups = session.Groups(6);
        var builds = await session.Fetch(session.Context(), groups, cancel);
        LiveLog.Line($"{providerId}: {builds.Count} builds from {groups.Count} fetches: {LiveLog.Counts(builds)}");
        foreach (var build in LiveSandbox.Newest(builds).Take(10))
        {
            LiveLog.Detail($"  {build.PipelineName} {build.Branch} {LiveLog.Row(build)} {build.BuildUrl}");
        }

        var requested = groups
            .SelectMany(_ => _.Pipelines)
            .Select(_ => _.Id)
            .ToHashSet();
        var foreign = builds.Count(_ => _.ConnectionId != live.Connection.Id ||
                                        !requested.Contains(_.PipelineId));
        await Assert.That(foreign).IsEqualTo(0).Because($"{providerId}: a build for a pipeline nobody asked for lands on a row that does not exist");
        var unlinked = builds.Count(_ => !Absolute(_.BuildUrl));
        await Assert.That(unlinked).IsEqualTo(0).Because($"{providerId}: a build link that is not an absolute http(s) URL opens nothing");
        var unactionable = builds.Count(_ => _.ProviderRef.Length == 0);
        await Assert.That(unactionable).IsEqualTo(0).Because($"{providerId}: a build without a ProviderRef cannot be retried, cancelled or have its log read");
        foreach (var unknown in builds.Where(_ => _.Status == BuildStatus.Unknown).DistinctBy(_ => _.StatusText).Take(3))
        {
            LiveLog.Warning($"{providerId}: a build's state '{unknown.StatusText}' maps to Unknown");
        }

        if (session.Sandbox is { } sandbox)
        {
            var unmapped = builds.Count(_ => _.PipelineId == sandbox.Id &&
                                            _.Status == BuildStatus.Unknown);
            await Assert.That(unmapped).IsEqualTo(0).Because($"{providerId}: every state the sandbox goes through should map to a status");
        }
    }

    /// <summary>
    /// A second fetch through the same cache sends every ETag the first got back. When the service
    /// answers every request 304, nothing changed, so the builds must be the same. Estimates are left
    /// out of that comparison, because they move with the clock.
    /// </summary>
    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task ConditionalFetch(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var session = await LiveSessions.Get(live, cancel);
        var groups = session.Groups(6);
        var cache = new ETagCache();
        using var cold = new CountingHandler(LiveConnection.Handler);
        var first = await session.Fetch(session.Context(cache, cold), groups, cancel);
        using var warm = new CountingHandler(LiveConnection.Handler);
        var second = await session.Fetch(session.Context(cache, warm), groups, cancel);
        LiveLog.Line($"{providerId}: cold {cold.Requests} requests, {cold.Tagged.Count} with an ETag. Warm {warm.Requests} requests, {warm.Revalidated.Count} revalidated, {warm.NotModified} not modified");
        var unrevalidated = cold.Tagged.Keys.Count(_ => warm.Requested.ContainsKey(_) &&
                                                        !warm.Revalidated.ContainsKey(_));
        await Assert.That(unrevalidated).IsEqualTo(0).Because($"{providerId}: a request that could have been conditional spends the quota again");
        if (warm.Requests == 0 ||
            warm.NotModified != warm.Requests)
        {
            return;
        }

        var same = first.Select(WithoutEstimate).SequenceEqual(second.Select(WithoutEstimate));
        await Assert.That(same).IsTrue().Because($"{providerId}: every request came back 304, so the builds should not have changed");
    }

    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task RecentActivity(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var session = await LiveSessions.Get(live, cancel);
        var groups = PollGroup.Of(live.Descriptor.FetchUnit, session.Pipelines);
        var context = session.Context(new());
        var activity = await live.Provider.RecentActivity(context, groups, ImmutableDictionary<string, string>.Empty, cancel);
        if (withoutActivity.Contains(providerId))
        {
            await Assert.That(activity).IsNull().Because($"{providerId}: the provider now probes for activity, so the harness should check it");
            return;
        }

        if (activity is null)
        {
            Assert.Fail($"{providerId}: the probe answered nothing, so the poller stops probing");
            return;
        }

        var keys = groups.Select(_ => _.Key).ToHashSet();
        var matched = activity.Keys.Count(keys.Contains);
        LiveLog.Line($"{providerId}: {activity.Count} activity tokens for {groups.Length} poll groups, {matched} of them matching a group");
        if (activity.Count > 0)
        {
            await Assert.That(matched).IsGreaterThan(0).Because($"{providerId}: a token for no poll group never makes a group due");
        }

        // A token that changes with nothing new makes every probe look like news. A build that really
        // changes in between is allowed for, by trying again.
        var stable = false;
        for (var attempt = 0; attempt < 3 && !stable; attempt++)
        {
            var next = await live.Provider.RecentActivity(context, groups, activity, cancel);
            stable = next is not null &&
                     next.Count == activity.Count &&
                     next.All(_ => activity.TryGetValue(_.Key, out var token) && token == _.Value);
            activity = next ?? activity;
        }

        await Assert.That(stable).IsTrue().Because($"{providerId}: activity tokens kept changing across probes");
    }

    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task FailedBuildLog(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var session = await LiveSessions.Get(live, cancel);
        var context = session.Context();
        var builds = LiveSandbox.Newest(await session.Fetch(context, session.Groups(6), cancel));
        var sandbox = session.Sandbox;
        var failed = builds.FirstOrDefault(_ => _.LogCopyable() &&
                                                (sandbox is null || _.PipelineId == sandbox.Id));
        if (failed is null)
        {
            Skip.Test($"{providerId}: no failed build to read a log from ({LiveLog.Counts(builds)})");
        }

        var log = await live.Provider.FetchLog(context, failed, cancel);
        var marked = log.Contains(LiveSettings.Marker, StringComparison.Ordinal);
        LiveLog.Line($"{providerId}: the log of {LiveLog.Row(failed)} is {log.Length} characters, marker {(marked ? "found" : "missing")}");
        if (sandbox is null)
        {
            if (log.Length == 0)
            {
                LiveLog.Warning($"{providerId}: the failed build's log is empty. Copy log would copy nothing.");
            }

            return;
        }

        await Assert.That(log.Length).IsGreaterThan(0).Because($"{providerId}: Copy log would copy nothing for a failed build");
        await Assert.That(marked).IsTrue().Because($"{providerId}: the sandbox's failed log should contain '{LiveSettings.Marker}'");
    }

    /// <summary>
    /// The files the sandbox's failed build published, listed and then downloaded. Nothing else
    /// here asks a service for an artifact, so this is the only check that the ids a listing gives
    /// are ones the download route accepts.
    /// <para>
    /// The bytes are not read for the marker the log is. Several services answer with a zip of the
    /// build's files rather than the file itself, so what arrives is only the same file on some of
    /// them.
    /// </para>
    /// </summary>
    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ReadTimeout)]
    public async Task Artifacts(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        if (!live.Descriptor.HasArtifacts)
        {
            Skip.Test($"{providerId}: the service has no artifact API.");
        }

        var session = await LiveSessions.Get(live, cancel);
        var context = session.Context();
        var builds = LiveSandbox.Newest(await session.Fetch(context, session.Groups(6), cancel));
        var sandbox = session.Sandbox;
        var failed = builds.FirstOrDefault(_ => _.ArtifactsListable(live.Descriptor) &&
                                                (sandbox is null || _.PipelineId == sandbox.Id));
        if (failed is null)
        {
            Skip.Test($"{providerId}: no failed build to list the files of ({LiveLog.Counts(builds)})");
        }

        var artifacts = await live.Provider.ListArtifacts(context, failed, cancel);
        LiveLog.Line($"{providerId}: {LiveLog.Row(failed)} published {artifacts.Count} files");
        foreach (var listed in artifacts.Take(10))
        {
            LiveLog.Detail($"  {listed.Name} {listed.Bytes?.ToString() ?? "size not given"} {listed.Unavailable}");
        }

        // A sandbox that publishes nothing has its files reported rather than checked, so each
        // provider is covered as its sandbox is taught to publish one.
        if (live.Artifact is not { } wanted)
        {
            LiveLog.Warning($"{providerId}: {LiveSettings.Prefix(providerId)}ARTIFACT names no file, so nothing here was downloaded");
            return;
        }

        var artifact = artifacts.FirstOrDefault(_ => _.Name == wanted);
        if (artifact is null)
        {
            Assert.Fail($"{providerId}: the sandbox should publish '{wanted}' even though its build fails, and listed {LiveLog.Names(artifacts)}");
            return;
        }

        if (artifact.Unavailable is { } unavailable)
        {
            Assert.Fail($"{providerId}: the service still lists '{wanted}' but will not serve it: {unavailable}");
            return;
        }

        var destination = new MemoryStream();
        var written = await live.Provider.DownloadArtifact(context, failed, artifact, destination, 32 * 1024 * 1024, cancel);
        LiveLog.Line($"{providerId}: {artifact.Name} arrived as {written} bytes");
        await Assert.That(written).IsGreaterThan(0).Because($"{providerId}: an artifact of no bytes would be saved as an empty file");
        await Assert.That(written).IsEqualTo(destination.Length).Because($"{providerId}: the count returned is what the budget is spent against, so it has to be what was written");

        // The budget the collector spends file by file. A provider that never checks it downloads
        // the whole thing and leaves nothing for the rest.
        await Assert.That(async () => await live.Provider.DownloadArtifact(context, failed, artifact, new MemoryStream(), 1, cancel))
            .Throws<ArtifactTooLargeException>()
            .Because($"{providerId}: a file past the cap should stop rather than arrive in full");
    }

    /// <summary>
    /// The app's own poll cycle, twice. The clock moves on between the cycles, so the second one
    /// probes for activity and revalidates what the first cached. An immediate second cycle would do
    /// neither. The settings are the app's defaults, so the history cutoff applies as it does for a user.
    /// </summary>
    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.PollTimeout)]
    public async Task PollCycles(string providerId, Cancel cancel)
    {
        var live = LiveConnection.Require(providerId);
        var id = live.Connection.Id;
        var host = new SessionHost(SessionState.Start(new() { Connections = [live.Connection] }));
        var secrets = new MemorySecretStore();
        secrets.Write(SecretKeys.Token(id), live.Token);
        var now = DateTimeOffset.UtcNow;
        var poller = new ConnectionPoller(id, host, secrets, new(), LiveConnection.Handler, null, () => now);
        for (var cycle = 1; cycle <= 2; cycle++)
        {
            var watch = Stopwatch.StartNew();
            var health = await poller.PollOnce(cancel);
            var state = host.State.Connection(id)!;
            LiveLog.Line($"{providerId}: cycle {cycle} {health} in {watch.Elapsed.TotalSeconds:0.0}s, {state.Pipelines.Length} pipelines, {host.State.Builds.Length} builds, access {state.Access}");
            if (health != ConnectionHealth.Ok)
            {
                Assert.Fail($"{providerId}: poll cycle {cycle} ended {health}: {state.Error}");
                return;
            }

            now += TimeSpan.FromMinutes(2);
        }
    }

    static bool Absolute(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        parsed.Scheme is "http" or "https";

    static Build WithoutEstimate(Build build) =>
        build with
        {
            Estimate = null
        };
}
