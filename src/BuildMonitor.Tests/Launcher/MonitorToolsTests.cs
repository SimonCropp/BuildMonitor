/// <summary>
/// The MCP tools against an in-process tray: the real handler over the canonical state, with
/// no socket in between.
/// </summary>
public class MonitorToolsTests
{
    const string failingBuild = "gh/Verify/test.yml/feature/inline";

    static (MonitorTools Tools, SessionHost Host, List<string> Opened) Create(FakeHttpHandler? http = null, SessionState? state = null)
    {
        var host = new SessionHost(state ?? Fixtures.WithBuilds());
        var poller = new Poller(host, new MemorySecretStore(), new(), http ?? new FakeHttpHandler());
        var opened = new List<string>();
        var handler = new MessageHandler(
            host,
            poller,
            opened.Add,
            _ =>
            {
            },
            new(),
            () => Fixtures.Now);
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
                _.Builds.Single(_ => _.Key == failingBuild),
                _.Builds.Single(_ => _.Key == failingBuild) with
                {
                    ProviderRef = "VerifyTests/Verify|77|failure"
                })
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
        var failing = await tools.ListFailing(null, Cancel.None);
        await Assert.That(failing.Select(_ => _.Key)).IsEquivalentTo(["gh/Verify/test.yml/feature/inline"]);
    }

    /// <summary>
    /// A pipeline's other branches follow its own build in the list, marked, and a pull request
    /// failing under a passing main is not a failing pipeline: the summary and the tray say the
    /// same, and triage would otherwise go looking for a break on main that is not there.
    /// </summary>
    [Test]
    public async Task OtherBranchesAreMarkedAndNotFailingPipelines()
    {
        var (tools, _, _) = Create(state: Fixtures.WithLanes());
        var builds = (await tools.ListBuilds(null, Cancel.None)).Where(_ => _.Key.StartsWith("gh/Verify/"));
        await Assert.That(builds.Select(_ => $"{_.Key} {_.OtherBranch}"))
            .IsEquivalentTo(
            [
                "gh/Verify/test.yml/main ",
                "gh/Verify/test.yml/dependabot/nuget/src/Polyfill-9.1.0 True",
                "gh/Verify/test.yml/feature/docs True",
                "gh/Verify/test.yml/feature/inline True"
            ]);
        await Assert.That(await tools.ListFailing(null, Cancel.None)).IsEmpty();
        await Assert.That((await tools.Summary(Cancel.None)).Failing).IsEqualTo(0);
    }

    /// <summary>
    /// A lane shows the pull request an older run of its branch named, as AppVeyor's branch build
    /// and pull request build of one commit split it between them. Opening it by the key
    /// list_builds gave goes through the row, not the first run held on that key, which names none.
    /// </summary>
    [Test]
    public async Task OpeningThePullRequestALaneBorrowed()
    {
        var branchBuild = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "feature/inline", "78", BuildStatus.Running, started: Fixtures.Now) with
        {
            DefaultBranch = "main"
        };
        var state = MonitorSession.ApplyPoll(Fixtures.WithBuilds(), Fixtures.GitHub.Id, [], [branchBuild, .. Fixtures.GitHubBuildsOnMain()], Fixtures.Now);
        var (tools, _, opened) = Create(state: state);
        await tools.OpenBuild("gh/Verify/test.yml/feature/inline", "pr", Cancel.None);
        await Assert.That(opened).IsEquivalentTo(["https://github.com/VerifyTests/Verify/pull/42"]);
    }

    /// <summary>
    /// The same filter list_builds takes, so the triage command can be pointed at one connection or
    /// one repository rather than at every pipeline being watched.
    /// </summary>
    [Test]
    public async Task ListFailingTakesTheSameFilterAsListBuilds()
    {
        var (tools, _, _) = Create();
        await Assert.That((await tools.ListFailing("verify", Cancel.None)).Select(_ => _.Key))
            .IsEquivalentTo(["gh/Verify/test.yml/feature/inline"]);
        await Assert.That(await tools.ListFailing("diffengine", Cancel.None)).IsEmpty();
    }

    /// <summary>
    /// Through the prompt rather than the projection, so a star that reached the tray as a literal
    /// filter, and so matched nothing, would fail here.
    /// </summary>
    [Test]
    public async Task TriageTakesAStarForEveryBuild()
    {
        var (tools, _, _) = Create();
        var prompts = new BuildPrompts(tools);

        await Assert.That(await prompts.Triage("*", "true", Cancel.None))
            .StartsWith("One failing build, none with a repository checked out locally");
        await Assert.That(await prompts.Triage("diffengine", null, Cancel.None))
            .IsEqualTo("Nothing is failing matching \"diffengine\". There is no triage to do.");
    }

    /// <summary>
    /// A mistyped id is refused, where it used to be answered as though its poll had started.
    /// </summary>
    [Test]
    public async Task RefreshRefusesAConnectionThatDoesNotExist()
    {
        var (tools, _, _) = Create();
        await Assert.That(await tools.Refresh(null, Cancel.None)).IsEqualTo("Refreshing every connection");
        await Assert.That(await tools.Refresh("gh", Cancel.None)).IsEqualTo("Refreshing gh");

        var exception = await Assert.That(async () => await tools.Refresh("nope", Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("No connection with id nope");
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
        await Assert.That(
            builds.Count(_ => _ is
            {
                Pipeline: "test.yml",
                Repo: "VerifyTests/Verify"
            }))
            .IsEqualTo(1);
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
        await Assert.That(string.Join('\n', pipelines.Select(_ => $"{_.Key} {_.Repo} {_.Runs}")))
            .IsEqualTo(
                """
                gh/DiffEngine/test.yml VerifyTests/DiffEngine 1
                gh/DiffEngine/docs.yml VerifyTests/DiffEngine 1
                gh/Verify/test.yml VerifyTests/Verify 2
                gh/Verify/release.yml VerifyTests/Verify 0
                """);
    }

    /// <summary>
    /// So an assistant reading a failure can open the code that broke without being told where the
    /// checkout is. Absent, rather than null, for a repository the tray has not found: the code
    /// directory is unset for most users and an empty field on every build would be noise.
    /// </summary>
    [Test]
    public async Task BuildsAndPipelinesCarryTheLocalCheckout()
    {
        var (tools, host, _) = Create();
        WithPipelines(host);
        host.Mutate(_ => MonitorSession.ApplyLocalRepos(_, Fixtures.LocalRepoIndex()));

        var builds = await tools.ListBuilds(null, Cancel.None);
        await Assert.That(builds.Single(_ => _.Key == "gh/DiffEngine/test.yml/main").Directory).IsEqualTo("/code/DiffEngine");
        // Jenkins reports a job name rather than a slug, so this one matched on the folder's name.
        await Assert.That(builds.Single(_ => _.Key == "jenkins/build-all/main").Directory).IsEqualTo("/code/build-all");
        await Assert.That(builds.Single(_ => _.Key == "gh/Verify/test.yml/feature/inline").Directory).IsNull();

        var pipelines = await tools.ListPipelines(Cancel.None);
        await Assert.That(pipelines.Single(_ => _.Key == "gh/DiffEngine/docs.yml").Directory).IsEqualTo("/code/DiffEngine");
        await Assert.That(pipelines.Single(_ => _.Key == "gh/Verify/release.yml").Directory).IsNull();
    }

    [Test]
    public async Task SummaryConnectionsAndOpen()
    {
        var (tools, _, opened) = Create();
        var summary = await tools.Summary(Cancel.None);
        var connections = await tools.ListConnections(Cancel.None);
        var url = await tools.OpenBuild("gh/Verify/test.yml/feature/inline", "pr", Cancel.None);
        await Assert.That(opened).IsEquivalentTo([url]);
        await Verify(new
        {
            summary,
            connections,
            url
        });
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
