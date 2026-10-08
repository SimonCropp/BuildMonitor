public class CircleCiProviderTests
{
    const string api = "https://circleci.com/api";
    const string project = $"{api}/v2/project/gh/VerifyTests/DiffEngine";
    const string organizationPipelines = $"{api}/v2/pipeline?org-slug=gh%2FVerifyTests";

    const string running =
        """{"id":"p-120","number":120,"project_slug":"gh/VerifyTests/DiffEngine","state":"created","created_at":"2026-01-01T11:55:00Z","trigger":{"type":"webhook","actor":{"login":"simon"}},"vcs":{"provider_name":"GitHub","target_repository_url":"https://github.com/VerifyTests/DiffEngine","origin_repository_url":"https://github.com/VerifyTests/DiffEngine","branch":"main","revision":"abc","commit":{"subject":"Fix","body":""}}}""";

    const string failed =
        """{"id":"p-119","number":119,"project_slug":"gh/VerifyTests/DiffEngine","state":"created","created_at":"2026-01-01T10:00:00Z","trigger":{"type":"webhook","actor":{"login":"simon"}},"vcs":{"provider_name":"GitHub","target_repository_url":"https://github.com/VerifyTests/DiffEngine","origin_repository_url":"https://github.com/VerifyTests/DiffEngine","branch":"feature","review_id":"7","review_url":"https://github.com/VerifyTests/DiffEngine/pull/7","revision":"def","commit":{"subject":"Feature","body":""}}}""";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{api}/v1.1/projects",
                """[{"vcs_url":"https://github.com/VerifyTests/DiffEngine","vcs_type":"github","username":"VerifyTests","reponame":"DiffEngine","default_branch":"main"}]""")
            .Get($"{api}/v2/me/collaborations", """[{"id":"o-1","vcs-type":"github","name":"VerifyTests","slug":"gh/VerifyTests","avatar_url":""}]""")
            .Get(organizationPipelines, $$"""{"items":[{{running}}],"next_page_token":null}""")
            .Get($"{project}/pipeline", $$"""{"items":[{{running}},{{failed}}],"next_page_token":null}""")
            .Get(
                $"{api}/v2/pipeline/p-120/workflow",
                """{"items":[{"id":"w-3","name":"build","status":"running","created_at":"2026-01-01T11:55:05Z","stopped_at":null}],"next_page_token":null}""")
            .Get(
                $"{api}/v2/pipeline/p-119/workflow",
                """
                {"items":[
                  {"id":"w-2","name":"build","status":"failed","created_at":"2026-01-01T10:00:05Z","stopped_at":"2026-01-01T10:10:00Z"},
                  {"id":"w-1","name":"docs","status":"success","created_at":"2026-01-01T10:00:06Z","stopped_at":"2026-01-01T10:05:00Z"}
                ],"next_page_token":null}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", ProviderTestHelpers.Context("circleci", handler));
        await Verify(new { builds, handler.Requests });
    }

    static async Task<BuildStatus?> StatusOf(string pipelineState, string workflows)
    {
        var handler = Handler()
            .Get($"{project}/pipeline", $$"""{"items":[{{running.Replace("\"state\":\"created\"", $"\"state\":\"{pipelineState}\"")}}]}""")
            .Get($"{api}/v2/pipeline/p-120/workflow", $$"""{"items":[{{workflows}}]}""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", ProviderTestHelpers.Context("circleci", handler));
        return builds.SingleOrDefault()?.Status;
    }

    static string Workflow(string name, string status, int minute = 0) =>
        $$"""{"id":"w-{{name}}-{{minute}}","name":"{{name}}","status":"{{status}}","created_at":"2026-01-01T11:{{minute:00}}:00Z","stopped_at":"2026-01-01T11:59:00Z"}""";

    /// <summary>
    /// A pipeline has no status: its workflows do. One still running keeps it running whatever the
    /// rest say, and one failed fails it however many passed.
    /// </summary>
    [Test]
    [Arguments("success", "running", BuildStatus.Running)]
    [Arguments("failed", "failing", BuildStatus.Running)]
    [Arguments("success", "failed", BuildStatus.Failed)]
    [Arguments("canceled", "error", BuildStatus.Failed)]
    [Arguments("success", "canceled", BuildStatus.Cancelled)]
    [Arguments("success", "not_run", BuildStatus.Succeeded)]
    // Waiting to be approved, for as long as nobody does: the build before it has passed.
    [Arguments("success", "on_hold", BuildStatus.Succeeded)]
    [Arguments("on_hold", "on_hold", BuildStatus.Unknown)]
    public async Task APipelinesStatusIsItsWorkflows(string first, string second, BuildStatus expected) =>
        await Assert.That(await StatusOf("created", $"{Workflow("build", first)},{Workflow("deploy", second)}")).IsEqualTo(expected);

    /// <summary>
    /// A rerun adds a workflow of the same name to the pipeline, and the failed one it reran stays
    /// listed beside it. Read with the rest, the pipeline stayed failed after its rerun passed.
    /// </summary>
    [Test]
    public async Task ARerunReplacesTheWorkflowItReran() =>
        await Assert.That(await StatusOf("created", $"{Workflow("build", "success", 30)},{Workflow("build", "failed", 10)}")).IsEqualTo(BuildStatus.Succeeded);

    /// <summary>
    /// A configuration that filters every workflow out leaves a pipeline that ran nothing, which
    /// would otherwise sit on the row as a build forever queued. One whose configuration did not
    /// compile has no workflows either, and that one did fail.
    /// </summary>
    [Test]
    [Arguments("created", null)]
    [Arguments("errored", BuildStatus.Failed)]
    public async Task APipelineWithNoWorkflowsIsABuildOnlyWhenItErrored(string state, BuildStatus? expected) =>
        await Assert.That(await StatusOf(state, "")).IsEqualTo(expected);

    /// <summary>
    /// An organization on CircleCI's GitHub App or GitLab is in no v1.1 listing, and v2 lists no
    /// projects, so its projects are found by their recent pipelines and asked for by slug, once.
    /// </summary>
    [Test]
    public async Task AProjectNoListingHasIsFoundByItsPipelines()
    {
        const string slug = "circleci/org-id/project-id";
        var handler = new FakeHttpHandler()
            .Get($"{api}/v1.1/projects", "[]")
            .Get($"{api}/v2/me/collaborations", """[{"id":"org-id","vcs-type":"circleci","name":"Team","slug":"circleci/org-id","avatar_url":""}]""")
            .Get(
                $"{api}/v2/pipeline?org-slug=circleci%2Forg-id",
                $$"""{"items":[{"id":"p-2","number":2,"project_slug":"{{slug}}","state":"created"},{"id":"p-1","number":1,"project_slug":"{{slug}}","state":"created"}]}""")
            .Get(
                $"{api}/v2/project/{slug}",
                $$"""{"slug":"{{slug}}","name":"app","organization_name":"Team","vcs_info":{"vcs_url":"//circleci.com/org-id/project-id","provider":"CircleCI","default_branch":"main"} }""");
        var context = ProviderTestHelpers.Context("circleci", handler);
        var provider = ProviderTestHelpers.Provider("circleci");
        var pipeline = (await provider.DiscoverPipelines(context, Cancel.None)).Single();
        await provider.DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipeline).IsEqualTo(new Pipeline(slug, "Team/app", "Team/app", null, $"https://app.circleci.com/pipelines/{slug}", DefaultBranch: "main"));
        await Assert.That(handler.Requests.Count(_ => _ == $"GET {api}/v2/project/{slug}")).IsEqualTo(1);
    }

    [Test]
    public async Task TheOrganizationScopeNarrowsBothListings()
    {
        var handler = Handler()
            .Get(
                $"{api}/v1.1/projects",
                """
                [
                  {"vcs_url":"https://github.com/VerifyTests/DiffEngine","vcs_type":"github","username":"VerifyTests","reponame":"DiffEngine","default_branch":"main"},
                  {"vcs_url":"https://github.com/Other/App","vcs_type":"github","username":"Other","reponame":"App","default_branch":"main"}
                ]
                """)
            .Get(
                $"{api}/v2/me/collaborations",
                """[{"name":"VerifyTests","slug":"gh/VerifyTests"},{"name":"Other","slug":"gh/Other"}]""");
        var context = ProviderTestHelpers.Context("circleci", handler, scope: ("organization", "gh/verifytests"));
        var pipelines = await ProviderTestHelpers.Provider("circleci").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines.Select(_ => _.Id)).IsEquivalentTo(["gh/VerifyTests/DiffEngine"]);
        await Assert.That(handler.Requests).DoesNotContain($"GET {api}/v2/pipeline?org-slug=gh%2FOther");
    }

    /// <summary>
    /// CircleCI server keeps its web app and its API on the one address.
    /// </summary>
    [Test]
    public async Task AServersLinksAreOnTheServer()
    {
        const string server = "https://circleci.example.com";
        var handler = new FakeHttpHandler()
            .Get($"{server}/api/v1.1/projects", """[{"vcs_url":"https://github.example.com/team/app","vcs_type":"github","username":"team","reponame":"app","default_branch":"main"}]""")
            .Get($"{server}/api/v2/me/collaborations", "[]");
        var context = ProviderTestHelpers.Context("circleci", handler, server);
        var pipeline = (await ProviderTestHelpers.Provider("circleci").DiscoverPipelines(context, Cancel.None)).Single();
        await Assert.That(pipeline.Url).IsEqualTo($"{server}/pipelines/github/team/app");
        await Assert.That(pipeline.RepoUrl).IsEqualTo("https://github.example.com/team/app");
    }

    /// <summary>
    /// A pull request from a fork is built as the pull request's own ref and names no review.
    /// </summary>
    [Test]
    public async Task AForksPullRequestIsReadFromItsBranch()
    {
        var handler = Handler()
            .Get($"{project}/pipeline", $$"""{"items":[{{running.Replace("\"branch\":\"main\"", "\"branch\":\"pull/12\"")}}]}""");
        var context = ProviderTestHelpers.Context("circleci", handler);
        var provider = ProviderTestHelpers.Provider("circleci");
        var pipelines = await provider.DiscoverPipelines(context, Cancel.None);
        // No default branch, so the pull request is all there is to fetch.
        var builds = await provider.FetchBuilds(context, [pipelines.Single() with { DefaultBranch = null }], 5, Cancel.None);
        var build = builds.Single();
        await Assert.That(build.Branch).IsEqualTo("pull/12");
        await Assert.That(build.BranchUrl).IsNull();
        await Assert.That(build.PullRequestNumber).IsEqualTo("12");
        await Assert.That(build.PullRequestUrl).IsEqualTo("https://github.com/VerifyTests/DiffEngine/pull/12");
    }

    const string mainPipelines = $"{project}/pipeline?branch=main";

    static FakeHttpHandler PullRequestsOnly() =>
        Handler()
            .Get($"{project}/pipeline", $$"""{"items":[{{failed}}]}""");

    [Test]
    public async Task PullRequestsFillingTheWindowFetchTheDefaultBranchsNewestPipeline()
    {
        var handler = PullRequestsOnly()
            .Get(mainPipelines, $$"""{"items":[{{running}}]}""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", ProviderTestHelpers.Context("circleci", handler));
        await Assert.That(builds.Select(_ => $"{_.RunNumber} {_.Branch}")).IsEquivalentTo(["119 feature", "120 main"]);
    }

    /// <summary>
    /// The page holds twenty pipelines and only the first few are shown, so a default branch
    /// further down it costs no request to find.
    /// </summary>
    [Test]
    public async Task TheDefaultBranchIsLookedForInThePageBeforeItIsAskedFor()
    {
        var handler = Handler()
            .Get($"{project}/pipeline", $$"""{"items":[{{failed}},{{running}}]}""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", ProviderTestHelpers.Context("circleci", handler), perPipeline: 1);
        await Assert.That(builds.Select(_ => $"{_.RunNumber} {_.Branch}")).IsEquivalentTo(["119 feature", "120 main"]);
        await Assert.That(handler.Requests).DoesNotContain($"GET {mainPipelines}");
    }

    [Test]
    public async Task AProjectWithNoPipelineOnItsDefaultBranchIsAskedOnceAnHour()
    {
        var handler = PullRequestsOnly()
            .Get(mainPipelines, """{"items":[]}""");
        var context = ProviderTestHelpers.Context("circleci", handler);
        await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        await Assert.That(handler.Requests.Count(_ => _ == $"GET {mainPipelines}")).IsEqualTo(1);
    }

    [Test]
    public async Task RecentActivityReadsTheNewestPipelineOfEachProject()
    {
        var handler = Handler()
            .Get(organizationPipelines, $$"""{"items":[{{running}},{{failed}}]}""");
        var context = ProviderTestHelpers.Context("circleci", handler);
        var provider = ProviderTestHelpers.Provider("circleci");
        await Assert.That(await provider.RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None)).IsNull();
        await provider.DiscoverPipelines(context, Cancel.None);
        handler.Requests.Clear();
        var activity = await provider.RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!.Count).IsEqualTo(1);
        await Assert.That(activity["gh/VerifyTests/DiffEngine"]).IsEqualTo("p-120");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {organizationPipelines}");
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("POST", $"{api}/v2/workflow/w-2/rerun", """{"workflow_id":"w-4"}""", HttpStatusCode.Accepted)
            .Map("POST", $"{api}/v2/workflow/w-3/cancel", """{"message":"Accepted."}""", HttpStatusCode.Accepted);
        var context = ProviderTestHelpers.Context("circleci", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("circleci");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "120"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://circleci.com/api/v2/pipeline/p-119/workflow,
                  POST https://circleci.com/api/v2/workflow/w-2/rerun
                  {"from_failed":true},
                  GET https://circleci.com/api/v2/pipeline/p-120/workflow,
                  POST https://circleci.com/api/v2/workflow/w-3/cancel
                ]
                """);
    }

    /// <summary>
    /// Nothing failed in a pipeline that passed, so there is no failed job to rerun from: every
    /// workflow is run again from its start.
    /// </summary>
    [Test]
    public async Task RetryOfAPassedPipelineRerunsEveryWorkflow()
    {
        var handler = Handler()
            .Get($"{api}/v2/pipeline/p-119/workflow", $$"""{"items":[{{Workflow("build", "success")}},{{Workflow("docs", "success")}}]}""")
            .Map("POST", $"{api}/v2/workflow/w-build-0/rerun", "{}", HttpStatusCode.Accepted)
            .Map("POST", $"{api}/v2/workflow/w-docs-0/rerun", "{}", HttpStatusCode.Accepted);
        var context = ProviderTestHelpers.Context("circleci", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("circleci").Retry(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://circleci.com/api/v2/pipeline/p-119/workflow,
                  POST https://circleci.com/api/v2/workflow/w-build-0/rerun
                  {"from_failed":false},
                  POST https://circleci.com/api/v2/workflow/w-docs-0/rerun
                  {"from_failed":false}
                ]
                """);
    }

    const string jobs =
        """
        {"items":[
          {"id":"j-1","name":"compile","job_number":501,"status":"success","type":"build"},
          {"id":"j-2","name":"test","job_number":502,"status":"failed","type":"build"},
          {"id":"j-3","name":"approve","status":"on_hold","type":"approval"}
        ],"next_page_token":null}
        """;

    [Test]
    public async Task FetchLogOfTheFailedStepsOfTheFailedJobs()
    {
        var handler = Handler()
            .Get($"{api}/v2/workflow/w-2/job", jobs)
            .Get(
                $"{api}/v1.1/project/gh/VerifyTests/DiffEngine/502",
                """
                {"steps":[
                  {"name":"Checkout code","actions":[{"step":101,"index":0,"failed":null,"status":"success","has_output":true,"output_url":"https://storage.example.com/signed"}]},
                  {"name":"npm test","actions":[{"step":102,"index":0,"failed":true,"status":"failed","has_output":true,"output_url":"https://storage.example.com/signed"}]}
                ]}
                """)
            .Get($"{api}/v1.1/project/gh/VerifyTests/DiffEngine/502/output/102/0?file=true", "1 test failed\n");
        var context = ProviderTestHelpers.Context("circleci", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("circleci").FetchLog(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> test / npm test <==\n1 test failed");
        // Never the signed address on the storage host, which the token would have gone to.
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            $"GET {api}/v2/pipeline/p-119/workflow",
            $"GET {api}/v2/workflow/w-2/job",
            $"GET {api}/v1.1/project/gh/VerifyTests/DiffEngine/502",
            $"GET {api}/v1.1/project/gh/VerifyTests/DiffEngine/502/output/102/0?file=true"
        ]);
    }

    const string artifactUrl = "https://output.circle-artifacts.com/output/job/j-2/artifacts/0/results/report.xml";

    static FakeHttpHandler Artifacts(string url) =>
        Handler()
            .Get($"{api}/v2/workflow/w-2/job", jobs)
            .Get($"{api}/v2/workflow/w-1/job", """{"items":[]}""")
            .Get($"{api}/v2/project/gh/VerifyTests/DiffEngine/501/artifacts", """{"items":[]}""")
            .Get(
                $"{api}/v2/project/gh/VerifyTests/DiffEngine/502/artifacts",
                $$"""{"items":[{"path":"results/report.xml","node_index":0,"url":"{{url}}"}],"next_page_token":null}""");

    [Test]
    public async Task ListAndDownloadArtifacts()
    {
        var handler = Artifacts(artifactUrl)
            .MapBytes("GET", artifactUrl, [1, 2, 3], "application/xml");
        var context = ProviderTestHelpers.Context("circleci", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        var build = builds.Single(_ => _.RunNumber == "119");
        var provider = ProviderTestHelpers.Provider("circleci");
        var artifact = (await provider.ListArtifacts(context, build, Cancel.None)).Single();
        await Assert.That(artifact.Name).IsEqualTo("results/report.xml");
        await Assert.That(artifact.Bytes).IsNull();
        using var destination = new MemoryStream();
        var written = await provider.DownloadArtifact(context, build, artifact, destination, 100, Cancel.None);
        await Assert.That(written).IsEqualTo(3);
    }

    /// <summary>
    /// A download is from whatever address the listing gave, with the token on it. An address that
    /// is not CircleCI's is refused before anything is sent there.
    /// </summary>
    [Test]
    [Arguments("https://example.com/report.xml")]
    [Arguments("https://notcircle-artifacts.com/report.xml")]
    [Arguments("http://output.circle-artifacts.com/report.xml")]
    public async Task AnArtifactSomewhereElseIsNotSentTheToken(string url)
    {
        var handler = Artifacts(url);
        var context = ProviderTestHelpers.Context("circleci", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("circleci", context);
        var build = builds.Single(_ => _.RunNumber == "119");
        var provider = ProviderTestHelpers.Provider("circleci");
        var artifact = (await provider.ListArtifacts(context, build, Cancel.None)).Single();
        handler.Requests.Clear();
        await Assert.That(() => provider.DownloadArtifact(context, build, artifact, Stream.Null, 100, Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task SendsTheTokenInItsOwnHeader()
    {
        var handler = new FakeHttpHandler().Get($"{api}/v2/me", """{"id":"u-1","login":"simon","name":"Simon"}""");
        var context = ProviderTestHelpers.Context("circleci", handler);
        var test = await ProviderTestHelpers.Provider("circleci").Test(context, Cancel.None);
        await Assert.That(test.Message).IsEqualTo("Signed in as simon");
        var headers = handler.RequestHeaders.Single();
        await Assert.That(headers.GetValues("Circle-Token").Single()).IsEqualTo("secret");
        await Assert.That(headers.Authorization).IsNull();
    }
}
