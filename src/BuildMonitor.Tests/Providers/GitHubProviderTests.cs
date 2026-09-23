public class GitHubProviderTests
{
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                """
                [
                  {"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z","default_branch":"main"},
                  {"full_name":"SimonCropp/Forked","html_url":"https://github.com/SimonCropp/Forked","archived":false,"disabled":false,"fork":true,"pushed_at":"2099-01-01T00:00:00Z"},
                  {"full_name":"VerifyTests/Old","html_url":"https://github.com/VerifyTests/Old","archived":false,"disabled":false,"pushed_at":"2000-01-01T00:00:00Z"},
                  {"full_name":"VerifyTests/Archived","html_url":"https://github.com/VerifyTests/Archived","archived":true,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"}
                ]
                """)
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100",
                """
                {"total_count":2,"workflows":[
                  {"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"},
                  {"id":11,"name":"Old","path":".github/workflows/old.yml","state":"disabled_manually"}
                ]}
                """)
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=5",
                """
                {"total_count":4,"workflow_runs":[
                  {"id":500,"workflow_id":10,"run_number":1234,"status":"in_progress","conclusion":null,"head_branch":"main","head_sha":"0123456789abcdef","display_title":"Fix the thing","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/500","created_at":"2026-01-01T11:56:00Z","updated_at":"2026-01-01T11:57:00Z","run_started_at":"2026-01-01T11:57:00Z","actor":{"login":"SimonCropp"},"head_commit":{"message":"Fix the thing\n\nDetails"},"head_repository":{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine"},"pull_requests":[]},
                  {"id":499,"workflow_id":10,"run_number":1233,"status":"completed","conclusion":"failure","head_branch":"feature","head_sha":"fedcba9876543210","display_title":"Feature","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/499","created_at":"2026-01-01T11:00:00Z","updated_at":"2026-01-01T11:05:00Z","run_started_at":"2026-01-01T11:00:30Z","actor":{"login":"SimonCropp"},"head_commit":{"message":"Feature"},"head_repository":{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine"},"pull_requests":[{"number":42}]},
                  {"id":497,"workflow_id":10,"run_number":1232,"status":"completed","conclusion":"success","head_branch":"main","head_sha":"a7fe96d","display_title":"Use main","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/497","created_at":"2026-01-01T10:30:00Z","updated_at":"2026-01-01T10:35:00Z","run_started_at":"2026-01-01T10:30:30Z","actor":{"login":"someone"},"head_commit":{"message":"Use main"},"head_repository":{"full_name":"someone/DiffEngine","html_url":"https://github.com/someone/DiffEngine"},"pull_requests":[]},
                  {"id":498,"workflow_id":11,"run_number":7,"status":"completed","conclusion":"success","head_branch":"main","head_sha":"aaa","display_title":"Old","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/498","created_at":"2026-01-01T10:00:00Z","updated_at":"2026-01-01T10:05:00Z","run_started_at":"2026-01-01T10:00:00Z","pull_requests":[]}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", ProviderTestHelpers.Context("github", handler));
        await Verify(new
        {
            builds,
            handler.Requests
        });
    }

    /// <summary>
    /// Two pull requests fill Test's share of a page of four, and its run of main comes third. It
    /// was in the response already, so it is kept past the cap rather than asked for again.
    /// </summary>
    [Test]
    public async Task ARunOnTheDefaultBranchPastTheCapIsKept()
    {
        var handler = TwoWorkflows()
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=4",
                $$"""
                {"workflow_runs":[
                  {{Run(604, 10, "fix-b")}},
                  {{Run(603, 10, "fix-a")}},
                  {{Run(602, 10, "main")}},
                  {{Run(601, 12, "main")}}
                ]}
                """);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", ProviderTestHelpers.Context("github", handler), perPipeline: 2);
        await Assert.That(builds.Select(_ => $"{_.PipelineName} {_.Branch}")).IsEquivalentTo(["Test fix-b", "Test fix-a", "Test main", "Docs main"]);
        await Assert.That(handler.Requests.Any(_ => _.Contains("/actions/workflows/10/runs"))).IsFalse();
    }

    /// <summary>
    /// A page with no run of main for a workflow asks that workflow for its runs on main, which a
    /// fork's main matches too, so that one is passed over for the repository's own.
    /// </summary>
    [Test]
    public async Task ADefaultBranchRunIsAskedOfTheWorkflowWhenThePageHasNone()
    {
        var handler = OneWorkflowOfPullRequests()
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows/10/runs?branch=main&per_page=5",
                $$"""
                {"workflow_runs":[
                  {{Run(598, 10, "main", "someone/DiffEngine")}},
                  {{Run(590, 10, "main")}}
                ]}
                """);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", ProviderTestHelpers.Context("github", handler));
        await Assert.That(builds.Select(_ => $"#{_.RunNumber} {_.Branch}")).IsEquivalentTo(["#1604 fix-b", "#1603 fix-a", "#1590 main"]);
    }

    /// <summary>
    /// A workflow with no run on main, as one only pull requests trigger has, costs its request once
    /// an hour rather than each poll.
    /// </summary>
    [Test]
    public async Task AWorkflowWithNoRunOnTheDefaultBranchIsAskedOnceAnHour()
    {
        const string asked = "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows/10/runs?branch=main&per_page=5";
        var handler = OneWorkflowOfPullRequests()
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows/10/runs?branch=main&per_page=5", """{"workflow_runs":[]}""");
        var context = ProviderTestHelpers.Context("github", handler);
        await ProviderTestHelpers.DiscoverAndFetch("github", context);
        await ProviderTestHelpers.DiscoverAndFetch("github", context);
        await Assert.That(handler.Requests.Count(_ => _ == asked)).IsEqualTo(1);
    }

    static FakeHttpHandler Listing(string workflows) =>
        new FakeHttpHandler()
            .Get(
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                """[{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z","default_branch":"main"}]""")
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100", workflows);

    static FakeHttpHandler TwoWorkflows() =>
        Listing(
            """
            {"workflows":[
              {"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"},
              {"id":12,"name":"Docs","path":".github/workflows/docs.yml","state":"active"}
            ]}
            """);

    static FakeHttpHandler OneWorkflowOfPullRequests() =>
        Listing("""{"workflows":[{"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"}]}""")
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=5",
                $$"""
                {"workflow_runs":[
                  {{Run(604, 10, "fix-b")}},
                  {{Run(603, 10, "fix-a")}}
                ]}
                """);

    /// <summary>
    /// A finished run, numbered after its id so each reads apart, newer for a higher id.
    /// </summary>
    static string Run(long id, long workflow, string branch, string repository = "VerifyTests/DiffEngine") =>
        $$"""
          {"id":{{id}},"workflow_id":{{workflow}},"run_number":{{id + 1000}},"status":"completed","conclusion":"success","head_branch":"{{branch}}","head_sha":"sha{{id}}","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/{{id}}","created_at":"2026-01-01T{{id / 60 % 24:00}}:{{id % 60:00}}:00Z","updated_at":"2026-01-01T{{id / 60 % 24:00}}:{{id % 60:00}}:30Z","run_started_at":"2026-01-01T{{id / 60 % 24:00}}:{{id % 60:00}}:00Z","head_repository":{"full_name":"{{repository}}","html_url":"https://github.com/{{repository}}"},"pull_requests":[]}
          """;

    [Test]
    public async Task DiscoveryListsWorkflowsOnlyForARepositoryPushedSince()
    {
        // Listing every repository's workflows each discovery cost a request a repository, which the
        // secondary limit counts even as a 304.
        const string listing = "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1";
        const string workflows = "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100";
        var handler = Handler();
        var context = ProviderTestHelpers.Context("github", handler);
        var provider = ProviderTestHelpers.Provider("github");
        await provider.DiscoverPipelines(context, Cancel.None);
        var again = await provider.DiscoverPipelines(context, Cancel.None);
        await Assert.That(again.Single().Name).IsEqualTo("Test");
        await Assert.That(handler.Requests.Count(_ => _.StartsWith(workflows, StringComparison.Ordinal))).IsEqualTo(1);

        handler.Get(listing, """[{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-02T00:00:00Z"}]""");
        await provider.DiscoverPipelines(context, Cancel.None);
        await Assert.That(handler.Requests.Count(_ => _.StartsWith(workflows, StringComparison.Ordinal))).IsEqualTo(2);
    }

    [Test]
    public async Task AnExcludedOrgIsNeverListed()
    {
        // What an org rule buys over the poller dropping the rows afterwards: the workflow listing
        // is a request a repository, which the secondary limit counts even as a 304.
        var handler = Handler();
        var context = ProviderTestHelpers.Context("github", handler) with
        {
            Filters = [new(FilterKind.Exact, FilterTarget.Org, "VerifyTests")]
        };
        var pipelines = await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines).IsEmpty();
        await Assert.That(handler.Requests.Any(_ => _.Contains("/actions/workflows"))).IsFalse();
    }

    [Test]
    public async Task HistoryLimitIsNotSentAsCreated()
    {
        // A run keeps its created_at through a re-run, so filtering on it would hide a run from
        // before the cutoff that is still running or was re-run since.
        var handler = Handler()
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs", """{"total_count":0,"workflow_runs":[]}""");
        var context = ProviderTestHelpers.Context("github", handler)
            with
            {
                Since = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)
            };
        await ProviderTestHelpers.DiscoverAndFetch("github", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("/actions/runs?per_page=5"))).IsTrue();
        await Assert.That(handler.Requests.Any(_ => _.Contains("/actions/runs") && _.Contains("created="))).IsFalse();
    }

    [Test]
    public async Task SkippedRunsAreLeftOut()
    {
        // A run every job skipped did nothing, and as the newest it would stand in for the last run
        // that actually built something.
        var handler = Handler()
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=5",
                """
                {"total_count":2,"workflow_runs":[
                  {"id":501,"workflow_id":10,"run_number":1235,"status":"completed","conclusion":"skipped","head_branch":"merge-dependabot","head_sha":"abc","display_title":"Skipped","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/501","created_at":"2026-01-01T12:00:00Z","updated_at":"2026-01-01T12:00:10Z","run_started_at":"2026-01-01T12:00:00Z","pull_requests":[]},
                  {"id":499,"workflow_id":10,"run_number":1233,"status":"completed","conclusion":"failure","head_branch":"main","head_sha":"fedcba9876543210","display_title":"Feature","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/499","created_at":"2026-01-01T11:00:00Z","updated_at":"2026-01-01T11:05:00Z","run_started_at":"2026-01-01T11:00:30Z","pull_requests":[]}
                ]}
                """);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", ProviderTestHelpers.Context("github", handler));
        await Assert.That(builds.Select(_ => _.RunNumber)).IsEquivalentTo(["1233"]);
    }

    [Test]
    public async Task ForksAndCollaborationsAreDiscoveredWhenAskedFor()
    {
        var handler = new FakeHttpHandler()
            .Get(
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member&page=1",
                """[{"full_name":"SimonCropp/Forked","html_url":"https://github.com/SimonCropp/Forked","archived":false,"disabled":false,"fork":true,"pushed_at":"2099-01-01T00:00:00Z"}]""")
            .Get(
                "https://api.github.com/repos/SimonCropp/Forked/actions/workflows?per_page=100",
                """{"total_count":1,"workflows":[{"id":1,"name":"Test","path":".github/workflows/test.yml","state":"active"}]}""");
        var context = ProviderTestHelpers.Context("github", handler) with
        {
            ShowForksAndCollaborations = true
        };
        var pipelines = await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines.Single().RepoName).IsEqualTo("SimonCropp/Forked");
    }

    [Test]
    public async Task RetryOfAFailureRerunsFailedJobs()
    {
        var handler = Handler()
            .Map("POST", "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/rerun-failed-jobs", "", HttpStatusCode.Created);
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("github").Retry(context, builds.Single(_ => _.RunNumber == "1233"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/rerun-failed-jobs
                ]
                """);
    }

    [Test]
    public async Task CancelARun()
    {
        var handler = Handler()
            .Map("POST", "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/500/cancel", "", HttpStatusCode.Accepted);
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("github").Cancel(context, builds.Single(_ => _.RunNumber == "1234"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/500/cancel
                ]
                """);
    }

    [Test]
    public async Task FetchLogOfTheJobsThatFailedOrTimedOut()
    {
        var handler = Handler()
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/jobs?filter=latest&per_page=100&page=1",
                """
                {"total_count":3,"jobs":[
                  {"id":71,"name":"build (ubuntu-latest)","conclusion":"success"},
                  {"id":72,"name":"build (windows-latest)","conclusion":"failure"},
                  {"id":73,"name":"docs","conclusion":"timed_out"}
                ]}
                """)
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/72/logs", "error CS1002: ; expected\n")
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/73/logs", "The job has exceeded the maximum execution time\n");
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("github").FetchLog(context, builds.Single(_ => _.RunNumber == "1233"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> build (windows-latest) <==\nerror CS1002: ; expected\n\n==> docs <==\nThe job has exceeded the maximum execution time");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/jobs?filter=latest&per_page=100&page=1",
            "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/72/logs",
            "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/73/logs"
        ]);
    }

    [Test]
    public async Task ListArtifactsOfARun()
    {
        var handler = Handler()
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/artifacts?per_page=100&page=1",
                """
                {"total_count":3,"artifacts":[
                  {"id":81,"name":"test-results","size_in_bytes":2048,"expired":false},
                  {"id":82,"name":"coverage","size_in_bytes":4096,"expired":false},
                  {"id":83,"name":"old-logs","size_in_bytes":512,"expired":true}
                ]}
                """);
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        var artifacts = await ProviderTestHelpers.Provider("github").ListArtifacts(context, builds.Single(_ => _.RunNumber == "1233"), Cancel.None);
        await Verify(new
            {
                artifacts,
                handler.Requests
            })
            .Snapshot(
                """
                {
                  artifacts: [
                    {
                      Id: 81,
                      Name: test-results.zip,
                      Bytes: 2048
                    },
                    {
                      Id: 82,
                      Name: coverage.zip,
                      Bytes: 4096
                    },
                    {
                      Id: 83,
                      Name: old-logs.zip,
                      Bytes: 512,
                      Unavailable: expired
                    }
                  ],
                  Requests: [
                    GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/artifacts?per_page=100&page=1
                  ]
                }
                """);
    }

    [Test]
    public async Task DownloadAnArtifact()
    {
        var handler = Handler()
            .MapBytes("GET", "https://api.github.com/repos/VerifyTests/DiffEngine/actions/artifacts/81/zip", [80, 75, 3, 4]);
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        handler.RequestHeaders.Clear();
        using var destination = new MemoryStream();
        var written = await ProviderTestHelpers.Provider("github").DownloadArtifact(
            context,
            builds.Single(_ => _.RunNumber == "1233"),
            new("81", "test-results.zip", 4),
            destination,
            ArtifactPlan.DefaultPerFile,
            Cancel.None);
        await Assert.That(written).IsEqualTo(4);
        await Assert.That(destination.ToArray()).IsEquivalentTo(new byte[]
        {
            80,
            75,
            3,
            4
        });
        // Anything, so the redirect to blob storage is not refused over a content type.
        await Assert.That(handler.RequestHeaders.Single().Accept.ToString()).IsEqualTo("*/*");
        await Assert.That(handler.Requests).IsEquivalentTo(["GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/artifacts/81/zip"]);
    }

    [Test]
    public async Task OwnerScopeUsesTheOrganization()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/orgs/VerifyTests/repos?per_page=100&sort=pushed&type=all&page=1", "[]");
        var context = ProviderTestHelpers.Context("github", handler, scope: ("owner", "VerifyTests"));
        var pipelines = await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines).IsEmpty();
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://api.github.com/orgs/VerifyTests/repos?per_page=100&sort=pushed&type=all&page=1
                ]
                """);
    }

    [Test]
    public async Task OwnerFallsBackToAUser()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/orgs/SimonCropp/repos?per_page=100&sort=pushed&type=all&page=1", "{}", HttpStatusCode.NotFound)
            .Get("https://api.github.com/users/SimonCropp/repos?per_page=100&sort=pushed&page=1", "[]");
        var context = ProviderTestHelpers.Context("github", handler, scope: ("owner", "SimonCropp"));
        await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://api.github.com/orgs/SimonCropp/repos?per_page=100&sort=pushed&type=all&page=1,
                  GET https://api.github.com/users/SimonCropp/repos?per_page=100&sort=pushed&page=1
                ]
                """);
    }

    [Test]
    public async Task EnterpriseServerUsesApiV3()
    {
        var provider = ProviderTestHelpers.Provider("github");
        var address = provider.BaseAddress(
            new()
            {
                Id = "x",
                ProviderId = "github",
                Name = "x",
                Server = "https://github.example.com"
            });
        await Assert.That(address.ToString()).IsEqualTo("https://github.example.com/api/v3/");
    }

    const string verifyRepository = "https://github.com/VerifyTests/Verify";

    static Task<BranchFate> FateOf(FakeHttpHandler handler, string branch, string? pullRequest = null, string repository = verifyRepository, string? server = null) =>
        ProviderTestHelpers.Provider("github").FateOf(ProviderTestHelpers.Context("github", handler, server), new(repository, branch, pullRequest), Cancel.None);

    /// <summary>
    /// A pull request is asked for its own state, whatever built it.
    /// </summary>
    [Test]
    [Arguments("""{"number":42,"state":"open","merged_at":null}""", BranchFate.Open)]
    [Arguments("""{"number":42,"state":"closed","merged_at":"2026-01-01T10:00:00Z"}""", BranchFate.Merged)]
    [Arguments("""{"number":42,"state":"closed","merged_at":null}""", BranchFate.Closed)]
    public async Task APullRequestIsAskedForItsState(string body, BranchFate fate)
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/pulls/42", body);
        await Assert.That(await FateOf(handler, "feature/inline", "42")).IsEqualTo(fate);
        await Assert.That(handler.Requests).IsEquivalentTo(["GET https://api.github.com/repos/VerifyTests/Verify/pulls/42"]);
    }

    /// <summary>
    /// A pull request the credential cannot see is a 404, which says nothing about it.
    /// </summary>
    [Test]
    public async Task APullRequestItCannotSeeCannotSay() =>
        await Assert.That(await FateOf(new(), "feature/inline", "42")).IsEqualTo(BranchFate.Unknown);

    /// <summary>
    /// A fork's branch builds here only through a pull request, and a run from one names none, so
    /// the newest pull request from that branch says what became of it.
    /// </summary>
    [Test]
    public async Task AForksBranchIsAskedForTheNewestPullRequestFromIt()
    {
        var handler = new FakeHttpHandler()
            .Get(
                "https://api.github.com/repos/VerifyTests/Verify/pulls?head=someone%3Afix-1834&state=all&per_page=1",
                """[{"number":1910,"state":"closed","merged_at":"2026-01-01T10:00:00Z"}]""");
        await Assert.That(await FateOf(handler, "someone:fix-1834")).IsEqualTo(BranchFate.Merged);
    }

    [Test]
    public async Task AForksBranchNoPullRequestNamesCannotSay()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/pulls?head=someone%3Afix-1834&state=all&per_page=1", "[]");
        await Assert.That(await FateOf(handler, "someone:fix-1834")).IsEqualTo(BranchFate.Unknown);
    }

    /// <summary>
    /// A branch with no pull request is asked whether it is still there, slashes and all.
    /// </summary>
    [Test]
    public async Task ABranchStillThereIsOpen()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/branches/dependabot/nuget/Polyfill-9.1.0", """{"name":"dependabot/nuget/Polyfill-9.1.0"}""");
        await Assert.That(await FateOf(handler, "dependabot/nuget/Polyfill-9.1.0")).IsEqualTo(BranchFate.Open);
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A release workflow's run is named for its tag, which is no branch but is still there. The
    /// listing matches every tag the name starts, so v1 lists v1.1 too.
    /// </summary>
    [Test]
    public async Task ATagOfTheNameIsOpen()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/git/matching-refs/tags/v1", """[{"ref":"refs/tags/v1"},{"ref":"refs/tags/v1.1"}]""");
        await Assert.That(await FateOf(handler, "v1")).IsEqualTo(BranchFate.Open);
    }

    /// <summary>
    /// Neither a branch nor a tag of the name, in a repository the credential can see, is a branch
    /// deleted. A tag that only starts with the name is another tag.
    /// </summary>
    [Test]
    public async Task NeitherBranchNorTagIsDeleted()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/git/matching-refs/tags/v1", """[{"ref":"refs/tags/v1.1"}]""");
        await Assert.That(await FateOf(handler, "v1")).IsEqualTo(BranchFate.Deleted);
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://api.github.com/repos/VerifyTests/Verify/branches/v1",
            "GET https://api.github.com/repos/VerifyTests/Verify/git/matching-refs/tags/v1"
        ]);
    }

    /// <summary>
    /// A repository the credential cannot see answers the branch and the tags alike with a 404, which
    /// must not read as the branch being deleted.
    /// </summary>
    [Test]
    public async Task ARepositoryItCannotSeeCannotSay() =>
        await Assert.That(await FateOf(new(), "feature/x")).IsEqualTo(BranchFate.Unknown);

    /// <summary>
    /// An address that names no repository is not asked about at all.
    /// </summary>
    [Test]
    public async Task AnAddressThatIsNoRepositoryIsNotAsked()
    {
        var handler = new FakeHttpHandler();
        await Assert.That(await FateOf(handler, "main", repository: "https://github.com/VerifyTests")).IsEqualTo(BranchFate.Unknown);
        await Assert.That(handler.Requests).IsEmpty();
    }

    /// <summary>
    /// GoCD names a repository by the address its git material clones, .git and all.
    /// </summary>
    [Test]
    public async Task ACloneAddressIsAskedAboutItsRepository()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/repos/VerifyTests/Verify/pulls/42", """{"number":42,"state":"open"}""");
        await Assert.That(await FateOf(handler, "fix", "42", "https://github.com/VerifyTests/Verify.git")).IsEqualTo(BranchFate.Open);
    }

    /// <summary>
    /// An enterprise server's repository is asked of that server's API.
    /// </summary>
    [Test]
    public async Task AnEnterpriseRepositoryIsAskedOfItsServer()
    {
        var handler = new FakeHttpHandler()
            .Get("https://github.example.com/api/v3/repos/team/app/pulls/3", """{"number":3,"state":"open"}""");
        await Assert.That(await FateOf(handler, "fix", "3", "https://github.example.com/team/app", "https://github.example.com")).IsEqualTo(BranchFate.Open);
    }

    /// <summary>
    /// GitHub Enterprise serves its pages on its own host, which the repository listing names.
    /// Composed on github.com, a pull request's link opened a repository github.com does not have,
    /// as did a branch's wherever the run gave no head repository page to open it in.
    /// </summary>
    [Test]
    public async Task EnterpriseServerLinksAreOnItsOwnHost()
    {
        var handler = new FakeHttpHandler()
            .Get(
                "https://github.example.com/api/v3/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                """[{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.example.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z","default_branch":"main"}]""")
            .Get(
                "https://github.example.com/api/v3/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100",
                """{"workflows":[{"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"}]}""")
            .Get(
                "https://github.example.com/api/v3/repos/VerifyTests/DiffEngine/actions/runs?per_page=5",
                """
                {"workflow_runs":[
                  {"id":499,"workflow_id":10,"run_number":1233,"status":"completed","conclusion":"failure","head_branch":"feature","html_url":"https://github.example.com/VerifyTests/DiffEngine/actions/runs/499","head_repository":{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.example.com/VerifyTests/DiffEngine"},"pull_requests":[{"number":42}]},
                  {"id":498,"workflow_id":10,"run_number":1232,"status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.example.com/VerifyTests/DiffEngine/actions/runs/498","head_repository":{"full_name":"someone/DiffEngine"},"pull_requests":[]},
                  {"id":497,"workflow_id":10,"run_number":1231,"status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.example.com/VerifyTests/DiffEngine/actions/runs/497","pull_requests":[]}
                ]}
                """);
        var context = ProviderTestHelpers.Context("github", handler, "https://github.example.com");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        await Assert.That(builds.Single(_ => _.RunNumber == "1233").PullRequestUrl).IsEqualTo("https://github.example.com/VerifyTests/DiffEngine/pull/42");
        await Assert.That(builds.Select(_ => $"{_.Branch} {_.BranchUrl}")).IsEquivalentTo(
        [
            "feature https://github.example.com/VerifyTests/DiffEngine/tree/feature",
            "someone:main https://github.example.com/someone/DiffEngine/tree/main",
            "main https://github.example.com/VerifyTests/DiffEngine/tree/main"
        ]);
        await Assert.That(builds.Select(_ => _.RepoUrl).Distinct().Single()).IsEqualTo("https://github.example.com/VerifyTests/DiffEngine");
    }

    [Test]
    public async Task UnauthorizedIsAnAuthException()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var context = ProviderTestHelpers.Context("github", handler);
        var exception = await Assert.That(async () => await ProviderTestHelpers.Provider("github").Test(context, Cancel.None)).Throws<AuthException>();
        await Assert.That(exception!.Message).Contains("401");
    }

    [Test]
    public async Task ExhaustedRateLimitIsARateLimitException()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"message":"API rate limit exceeded"}""", HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "0"), ("Retry-After", "120"));
        var context = ProviderTestHelpers.Context("github", handler);
        var exception = await Assert.That(async () => await ProviderTestHelpers.Provider("github").Test(context, Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(120));
    }

    [Test]
    public async Task RecentActivityReadsTheFirstPageOfTheListing()
    {
        var handler = Handler();
        var context = ProviderTestHelpers.Context("github", handler);
        var activity = await ProviderTestHelpers.Provider("github").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!["VerifyTests/DiffEngine"]).IsEqualTo("2099-01-01T00:00:00.0000000+00:00");
        await Assert.That(handler.Requests.Single()).IsEqualTo("GET https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1");
    }

    [Test]
    public async Task RecentActivitySharesTheDiscoveryETag()
    {
        const string listing = "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1";
        var handler = new FakeHttpHandler().Map("GET", listing, "[]", HttpStatusCode.OK, ("ETag", "\"repos\""));
        var context = ProviderTestHelpers.Context("github", handler);
        var provider = ProviderTestHelpers.Provider("github");
        await provider.DiscoverPipelines(context, Cancel.None);
        handler.Map("GET", listing, "", HttpStatusCode.NotModified);
        await provider.RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(handler.Requests[^1]).IsEqualTo($"GET {listing}\n  If-None-Match: \"repos\"");
    }

    [Test]
    public async Task RecentActivityUsesTheUserListingOnceDiscoveryFoundIt()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/orgs/SimonCropp/repos?per_page=100&sort=pushed&type=all&page=1", "{}", HttpStatusCode.NotFound)
            .Map("GET", "https://api.github.com/users/SimonCropp/repos?per_page=100&sort=pushed&page=1", "[]", HttpStatusCode.OK, ("ETag", "\"user\""));
        var context = ProviderTestHelpers.Context("github", handler, scope: ("owner", "SimonCropp"));
        var provider = ProviderTestHelpers.Provider("github");
        await provider.DiscoverPipelines(context, Cancel.None);
        handler.Requests.Clear();
        await provider.RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(handler.Requests.Single()).StartsWith("GET https://api.github.com/users/SimonCropp/repos");
    }

    [Test]
    public async Task NotModifiedComesFromTheCache()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("ETag", "\"abc\""));
        var context = ProviderTestHelpers.Context("github", handler);
        var provider = ProviderTestHelpers.Provider("github");
        var first = await provider.Test(context, Cancel.None);

        handler.Map("GET", "https://api.github.com/user", "", HttpStatusCode.NotModified);
        var second = await provider.Test(context, Cancel.None);

        await Assert.That(first.Message).IsEqualTo("Signed in as simon");
        await Assert.That(second.Message).IsEqualTo("Signed in as simon");
        // Each test also asks for the token's scopes, which is never revalidated.
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://api.github.com/user,
                  GET https://api.github.com/user,
                  GET https://api.github.com/user
                  If-None-Match: "abc",
                  GET https://api.github.com/user
                ]
                """);
    }

    [Test]
    [Arguments("repo, read:org", nameof(BuildAccess.Change))]
    [Arguments("repo:status, read:org, workflow", nameof(BuildAccess.Watch))]
    [Arguments("", nameof(BuildAccess.Watch))]
    [Arguments("public_repo", nameof(BuildAccess.Unknown))]
    public async Task ATokenListingItsScopesIsJudgedByThem(string scopes, string expected)
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("X-OAuth-Scopes", scopes));
        var context = ProviderTestHelpers.Context("github", handler);
        var access = await ProviderTestHelpers.Provider("github").Access(context, Cancel.None);
        await Assert.That(access.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task AFineGrainedTokenSaysNothing()
    {
        // A fine grained token and a GitHub App's send no scopes header.
        var handler = new FakeHttpHandler().Get("https://api.github.com/user", """{"login":"simon"}""");
        var context = ProviderTestHelpers.Context("github", handler);
        var access = await ProviderTestHelpers.Provider("github").Access(context, Cancel.None);
        await Assert.That(access).IsEqualTo(BuildAccess.Unknown);
    }

    [Test]
    [Arguments(false, BuildAccess.Change, false)]
    [Arguments(true, BuildAccess.Change, true)]
    [Arguments(false, BuildAccess.Unknown, true)]
    public async Task PushDecidesOnlyForATokenListingItsScopes(bool push, BuildAccess access, bool offered)
    {
        // Whose a fine grained token's permissions are is not documented, and it can re-run with
        // Actions write where push is false.
        var handler = Handler()
            .Get(
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                $$$"""[{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z","permissions":{"admin":false,"maintain":false,"push":{{{push.ToString().ToLowerInvariant()}}},"triage":false,"pull":true}}]""");
        var context = ProviderTestHelpers.Context("github", handler) with
        {
            Access = access
        };
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        await Assert.That(builds.Single(_ => _.RunNumber == "1234").CanCancel).IsEqualTo(offered);
        await Assert.That(builds.Single(_ => _.RunNumber == "1233").CanRetry).IsEqualTo(offered);
    }

    [Test]
    public async Task TheTestSaysATokenCanOnlyWatch()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("X-OAuth-Scopes", "read:org"));
        var context = ProviderTestHelpers.Context("github", handler);
        var result = await ProviderTestHelpers.Provider("github").Test(context, Cancel.None);
        await Assert.That(result.Describe(ProviderDescriptors.GitHub))
            .IsEqualTo("Signed in as simon. The connection can watch builds but not change them. GitHub Actions needs Actions read and write, or the repo scope on a classic token");
    }

    [Test]
    public async Task AFailedScopeCheckDoesNotFailTheTest()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("ETag", "\"abc\""));
        var context = ProviderTestHelpers.Context("github", handler);
        var provider = ProviderTestHelpers.Provider("github");
        await provider.Test(context, Cancel.None);
        // Revalidated, the user answers 304; the scope check, never revalidated, then fails.
        handler.Map("GET", "https://api.github.com/user", "", HttpStatusCode.NotModified);
        var result = await provider.Test(context, Cancel.None);
        await Assert.That(result.Ok).IsTrue();
        await Assert.That(result.Access).IsEqualTo(BuildAccess.Unknown);
    }
}
