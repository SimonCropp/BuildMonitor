/// <summary>
/// The MCP tools against an in-process tray: the real handler over the canonical state, with
/// no socket in between.
/// </summary>
public class MonitorToolsTests
{
    const string failingBuild = "gh/Verify/test.yml/feature/inline";

    static (MonitorTools Tools, SessionHost Host, List<string> Opened) Create(FakeHttpHandler? http = null)
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        var poller = new Poller(host, new MemorySecretStore(), new(), http ?? new FakeHttpHandler());
        var opened = new List<string>();
        var handler = new MessageHandler(host, poller, opened.Add, _ => { }, () => Fixtures.Now);
        return (new(new InProcessClient(handler)), host, opened);
    }

    /// <summary>
    /// The failing row carrying what a real poll would have left on it, which is what the
    /// provider composes the log's URLs from.
    /// </summary>
    static void WithProviderRef(SessionHost host) =>
        host.Mutate(_ => _ with
        {
            Builds = _.Builds.Replace(
                _.Builds.Single(build => build.Key == failingBuild),
                _.Builds.Single(build => build.Key == failingBuild) with { ProviderRef = "VerifyTests/Verify|77|failure" })
        });

    /// <summary>
    /// The pipelines a discovery leaves on the connection, which the shared fixture polls without.
    /// One of them has run nothing inside the window, which is the blind spot the rows have.
    /// </summary>
    static void WithPipelines(SessionHost host) =>
        host.Mutate(_ => MonitorSession.ApplyPoll(
            _,
            Fixtures.GitHub.Id,
            [
                new("DiffEngine/test.yml", "test.yml", "VerifyTests/DiffEngine", "DiffEngine", "https://github.com/VerifyTests/DiffEngine/actions/workflows/test.yml"),
                new("DiffEngine/docs.yml", "docs.yml", "VerifyTests/DiffEngine", "DiffEngine", "https://github.com/VerifyTests/DiffEngine/actions/workflows/docs.yml"),
                new("Verify/test.yml", "test.yml", "VerifyTests/Verify", "Verify", "https://github.com/VerifyTests/Verify/actions/workflows/test.yml"),
                new("Verify/release.yml", "release.yml", "VerifyTests/Verify", "Verify", "https://github.com/VerifyTests/Verify/actions/workflows/release.yml")
            ],
            Fixtures.GitHubBuilds(),
            Fixtures.Now - TimeSpan.FromSeconds(12)));

    static FakeHttpHandler WithJobs(string jobs, params (string Id, string Log)[] logs)
    {
        var http = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/actions/runs/77/jobs?filter=latest&per_page=100&page=1", jobs);
        foreach (var (id, log) in logs)
        {
            http.Get($"https://api.github.com/repos/VerifyTests/Verify/actions/jobs/{id}/logs", log);
        }

        return http;
    }

    [Test]
    public async Task ListBuildsWithAndWithoutFilter()
    {
        var (tools, _, _) = Create();
        var all = await tools.ListBuilds(null, Cancel.None);
        var filtered = await tools.ListBuilds("verify", Cancel.None);
        await Assert.That(all.Count).IsEqualTo(6);
        await Verify(filtered);
    }

    [Test]
    public async Task ListFailing()
    {
        var (tools, _, _) = Create();
        var failing = await tools.ListFailing(Cancel.None);
        await Assert.That(failing.Select(_ => _.Key)).IsEquivalentTo(["gh/Verify/test.yml/feature/inline"]);
    }

    [Test]
    public async Task GetBuildAndMissing()
    {
        var (tools, _, _) = Create();
        var build = await tools.GetBuild("jenkins/build-all/main", Cancel.None);
        await Assert.That(build.Timing).IsEqualTo("04:00 left");
        var exception = await Assert.That(async () => await tools.GetBuild("nope", Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("No build with key nope");
    }

    /// <summary>
    /// The row list shows one build for Verify's test.yml; the pipeline ran twice, and both are in
    /// the state the whole time.
    /// </summary>
    [Test]
    public async Task ListRunsUncollapsesThePipeline()
    {
        var (tools, _, _) = Create();
        var builds = await tools.ListBuilds(null, Cancel.None);
        var runs = await tools.ListRuns(failingBuild, Cancel.None);
        await Assert.That(builds.Count(_ => _.Pipeline == "test.yml" && _.Repo == "VerifyTests/Verify")).IsEqualTo(1);
        await Assert.That(string.Join(", ", runs.Select(_ => $"{_.Run} {_.Branch} {_.Status}")))
            .IsEqualTo("77 feature/inline Failed, 76 main Succeeded");
    }

    [Test]
    public async Task ListRunsTakesAPipelineKeyAndRefusesAnythingElse()
    {
        var (tools, _, _) = Create();
        var runs = await tools.ListRuns("gh/Verify/test.yml", Cancel.None);
        await Assert.That(string.Join(", ", runs.Select(_ => _.Run))).IsEqualTo("77, 76");
        var exception = await Assert.That(async () => await tools.ListRuns("nope", Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("No pipeline with key nope");
    }

    [Test]
    public async Task ListPipelinesCountsTheRunsAndKeepsTheQuietOne()
    {
        var (tools, host, _) = Create();
        WithPipelines(host);
        var pipelines = await tools.ListPipelines(Cancel.None);
        await Assert.That(string.Join("\n", pipelines.Select(_ => $"{_.Key} {_.Repo} {_.Runs}")))
            .IsEqualTo(
                """
                gh/DiffEngine/test.yml VerifyTests/DiffEngine 1
                gh/DiffEngine/docs.yml VerifyTests/DiffEngine 1
                gh/Verify/test.yml VerifyTests/Verify 2
                gh/Verify/release.yml VerifyTests/Verify 0
                """);
    }

    [Test]
    public async Task SummaryConnectionsAndOpen()
    {
        var (tools, _, opened) = Create();
        var summary = await tools.Summary(Cancel.None);
        var connections = await tools.ListConnections(Cancel.None);
        var url = await tools.OpenBuild("gh/Verify/test.yml/feature/inline", "pr", Cancel.None);
        await Assert.That(opened).IsEquivalentTo([url]);
        await Verify(new { summary, connections, url });
    }

    [Test]
    public async Task RefreshMessages()
    {
        var (tools, _, _) = Create();
        await Assert.That(await tools.Refresh(null, Cancel.None)).IsEqualTo("Refreshing every connection");
        await Assert.That(await tools.Refresh("gh", Cancel.None)).IsEqualTo("Refreshing gh");
    }

    [Test]
    public async Task GetLogTailsWhatTheFailedJobsWrote()
    {
        var (tools, host, _) = Create(WithJobs(
            """{"total_count":1,"jobs":[{"id":72,"name":"build","conclusion":"failure"}]}""",
            ("72", "restoring\nbuilding\nerror CS1002: ; expected\n")));
        WithProviderRef(host);
        var log = await tools.GetLog(failingBuild, 2, Cancel.None);
        await Assert.That(log).IsEqualTo("==> build <==\n... 1 earlier line dropped\nbuilding\nerror CS1002: ; expected");
    }

    [Test]
    public async Task GetLogOfARunWithNothingFailedSaysThereIsNone()
    {
        var (tools, host, _) = Create(WithJobs("""{"total_count":1,"jobs":[{"id":71,"name":"build","conclusion":"success"}]}"""));
        WithProviderRef(host);
        var exception = await Assert.That(async () => await tools.GetLog(failingBuild, 200, Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("That build has no log");
    }

    [Test]
    public async Task RetryARunningBuildIsRefused()
    {
        var (tools, _, _) = Create();
        var exception = await Assert.That(async () => await tools.RetryBuild("gh/DiffEngine/test.yml/main", Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("That build cannot be retried");
    }

    sealed class InProcessClient(MessageHandler handler) : IProtocolClient
    {
        public Task<Response> Send(Message message, Cancel cancel) =>
            handler.Handle(message);
    }
}
