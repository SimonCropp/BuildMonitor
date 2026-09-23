public class GitLabProviderTests
{
    const string graph = "https://gitlab.com/api/graphql";

    const string developerListing = "https://gitlab.com/api/v4/projects?membership=true&min_access_level=30&simple=true&archived=false&order_by=last_activity_at&per_page=100";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://gitlab.com/api/v4/projects?membership=true&min_access_level=20&simple=true&archived=false&order_by=last_activity_at&per_page=100",
                """[{"id":77,"path_with_namespace":"verify/diffengine","web_url":"https://gitlab.com/verify/diffengine","default_branch":"main"}]""")
            .Get(
                developerListing,
                """[{"id":77,"path_with_namespace":"verify/diffengine","web_url":"https://gitlab.com/verify/diffengine"}]""")
            .Get(
                graph,
                """
                {"data":{"projects":{"nodes":[{"id":"gid://gitlab/Project/77","pipelines":{"nodes":[
                  {"id":"gid://gitlab/Ci::Pipeline/5001","iid":"120","status":"RUNNING","ref":"main","sha":"abc123","createdAt":"2026-01-01T11:55:00Z","updatedAt":"2026-01-01T11:56:00Z","startedAt":"2026-01-01T11:55:30Z","finishedAt":null,"user":{"name":"Simon"}},
                  {"id":"gid://gitlab/Ci::Pipeline/5000","iid":"119","status":"FAILED","ref":"refs/merge-requests/9/head","sha":"def456","createdAt":"2026-01-01T10:00:00Z","updatedAt":"2026-01-01T10:08:00Z","startedAt":"2026-01-01T10:00:20Z","finishedAt":"2026-01-01T10:08:00Z","user":{"name":"Simon"},"mergeRequest":{"iid":"9","sourceBranch":"feature","sourceProject":{"fullPath":"verify/diffengine","webUrl":"https://gitlab.com/verify/diffengine"}}},
                  {"id":"gid://gitlab/Ci::Pipeline/4999","iid":"118","status":"SUCCESS","ref":"refs/merge-requests/11/head","sha":"a7fe96d","createdAt":"2026-01-01T09:00:00Z","updatedAt":"2026-01-01T09:08:00Z","startedAt":"2026-01-01T09:00:20Z","finishedAt":"2026-01-01T09:08:00Z","user":{"name":"Someone"},"mergeRequest":{"iid":"11","sourceBranch":"main","sourceProject":{"fullPath":"someone/diffengine","webUrl":"https://gitlab.com/someone/diffengine"}}}
                ]}}]}}}
                """);

    static FakeHttpHandler Rest(FakeHttpHandler handler) =>
        handler
            .Get(
                "https://gitlab.com/api/v4/projects/77/pipelines?per_page=5",
                """
                [
                  {"id":5001,"iid":120,"project_id":77,"status":"running","source":"push","ref":"main","sha":"abc123","web_url":"https://gitlab.com/verify/diffengine/-/pipelines/5001","created_at":"2026-01-01T11:55:00Z","updated_at":"2026-01-01T11:56:00Z","name":"Fix"},
                  {"id":5000,"iid":119,"project_id":77,"status":"failed","source":"merge_request_event","ref":"refs/merge-requests/9/head","sha":"def456","web_url":"https://gitlab.com/verify/diffengine/-/pipelines/5000","created_at":"2026-01-01T10:00:00Z","updated_at":"2026-01-01T10:08:00Z","name":null}
                ]
                """)
            .Get(
                "https://gitlab.com/api/v4/projects/77/pipelines/5001",
                """{"id":5001,"iid":120,"status":"running","ref":"main","sha":"abc123","web_url":"https://gitlab.com/verify/diffengine/-/pipelines/5001","created_at":"2026-01-01T11:55:00Z","updated_at":"2026-01-01T11:56:00Z","started_at":"2026-01-01T11:55:30Z","finished_at":null,"duration":null}""");

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Verify(new
        {
            builds,
            handler.Requests
        });
    }

    const string mainPipelines = "https://gitlab.com/api/v4/projects/77/pipelines?per_page=1&ref=main";

    /// <summary>
    /// A window of merge requests only asks for the newest pipeline on main by its ref, which leaves
    /// out the merge request pipelines, whose ref is the merge request's.
    /// </summary>
    [Test]
    public async Task MergeRequestsFillingTheWindowFetchTheDefaultBranchsNewestPipeline()
    {
        var handler = MergeRequestsOnly()
            .Get(
                mainPipelines,
                """[{"id":4990,"iid":110,"project_id":77,"status":"success","source":"push","ref":"main","sha":"999","web_url":"https://gitlab.com/verify/diffengine/-/pipelines/4990","created_at":"2025-12-31T10:00:00Z","updated_at":"2025-12-31T10:08:00Z","name":"Release"}]""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Assert.That(builds.Select(_ => $"{_.RunNumber} {_.Branch}")).IsEquivalentTo(["119 feature", "110 main"]);
    }

    /// <summary>
    /// A default branch with no pipeline on it answers empty, and the fetches within the hour do not
    /// ask again.
    /// </summary>
    [Test]
    public async Task AProjectWithNoPipelineOnItsDefaultBranchIsAskedOnceAnHour()
    {
        var handler = MergeRequestsOnly()
            .Get(mainPipelines, "[]");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        await Assert.That(handler.Requests.Count(_ => _ == $"GET {mainPipelines}")).IsEqualTo(1);
    }

    static FakeHttpHandler MergeRequestsOnly() =>
        Handler()
            .Get(
                graph,
                """
                {"data":{"projects":{"nodes":[{"id":"gid://gitlab/Project/77","pipelines":{"nodes":[
                  {"id":"gid://gitlab/Ci::Pipeline/5000","iid":"119","status":"FAILED","ref":"refs/merge-requests/9/head","sha":"def456","createdAt":"2026-01-01T10:00:00Z","updatedAt":"2026-01-01T10:08:00Z","startedAt":"2026-01-01T10:00:20Z","finishedAt":"2026-01-01T10:08:00Z","user":{"name":"Simon"},"mergeRequest":{"iid":"9","sourceBranch":"feature","sourceProject":{"fullPath":"verify/diffengine","webUrl":"https://gitlab.com/verify/diffengine"}}}
                ]}}]}}}
                """);

    [Test]
    public async Task HistoryLimitIsSentAsUpdatedAfter()
    {
        var handler = Handler();
        var context = ProviderTestHelpers.Context("gitlab", handler)
            with
            {
                Since = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)
            };
        await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("updatedAfter") && _.Contains("2026-08-16T00"))).IsTrue();
    }

    [Test]
    public async Task GraphQLErrorsFallBackToRest()
    {
        var handler = Rest(Handler().Get(graph, """{"errors":[{"message":"Field 'startedAt' doesn't exist on type 'Pipeline'"}]}"""));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Assert.That(builds.Select(_ => _.RunNumber)).IsEquivalentTo(["120", "119"]);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/pipelines?per_page=5"))).IsEqualTo(1);
    }

    [Test]
    public async Task AGraphQLAnswerThatIsAWebPageFallsBackToRest()
    {
        var handler = Rest(Handler().MapHtml("GET", graph, "<html>GitLab</html>"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Assert.That(builds.Select(_ => _.RunNumber)).IsEquivalentTo(["120", "119"]);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/pipelines?per_page=5"))).IsEqualTo(1);
    }

    [Test]
    public async Task AProjectLeftOutOfTheGraphQLAnswerIsFetchedOverRest()
    {
        var handler = Rest(Handler().Get(graph, """{"data":{"projects":{"nodes":[]}}}"""));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Assert.That(builds.Count).IsEqualTo(2);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/pipelines?per_page=5"))).IsEqualTo(1);
    }

    [Test]
    public async Task ProjectsAreAskedForFiftyAtATime()
    {
        var pipelines = Enumerable.Range(1, 51)
            .Select(_ => new Pipeline(_.ToString(), $"verify/p{_}", $"verify/p{_}", null, $"https://gitlab.com/verify/p{_}/-/pipelines"))
            .ToList();
        var nodes = string.Join(',', pipelines.Select(_ => $$$"""{"id":"gid://gitlab/Project/{{{_.Id}}}","pipelines":{"nodes":[]}}"""));
        var handler = new FakeHttpHandler().Get(graph, $$$$"""{"data":{"projects":{"nodes":[{{{{nodes}}}}]}}}""");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.Provider("gitlab").FetchBuilds(context, pipelines, 5, Cancel.None);
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(handler.Requests.All(_ => _.StartsWith($"GET {graph}?query=", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task GraphQLThatFailedIsNotAskedAgainForAnHour()
    {
        // Asked every poll, a server without GraphQL paid a failed request before the REST ones.
        var handler = Rest(Handler().MapHtml("GET", graph, "<html>GitLab</html>"));
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        await Assert.That(builds.Select(_ => _.RunNumber)).IsEquivalentTo(["120", "119"]);
        await Assert.That(handler.Requests.Count(_ => _.StartsWith($"GET {graph}", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/pipelines?per_page=5"))).IsEqualTo(2);
    }

    [Test]
    public async Task ProjectsAreAskedForInIdOrder()
    {
        // In discovery order, most recently active first, a rediscovery that reordered the projects
        // changed the request's URL and lost the ETag cached for it.
        var pipelines = new[]
            {
                "9",
                "10",
                "2"
            }
            .Select(_ => new Pipeline(_, $"verify/p{_}", $"verify/p{_}", null, $"https://gitlab.com/verify/p{_}/-/pipelines"))
            .ToList();
        var nodes = string.Join(',', pipelines.Select(_ => $$$"""{"id":"gid://gitlab/Project/{{{_.Id}}}","pipelines":{"nodes":[]}}"""));
        var handler = new FakeHttpHandler().Get(graph, $$$$"""{"data":{"projects":{"nodes":[{{{{nodes}}}}]}}}""");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.Provider("gitlab").FetchBuilds(context, pipelines, 5, Cancel.None);
        await Assert.That(Uri.UnescapeDataString(handler.Requests.Single())).Contains("""ids:["gid://gitlab/Project/2","gid://gitlab/Project/9","gid://gitlab/Project/10"]""");
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("POST", "https://gitlab.com/api/v4/projects/77/pipelines/5000/retry", "{}", HttpStatusCode.Created)
            .Map("POST", "https://gitlab.com/api/v4/projects/77/pipelines/5001/cancel", "{}");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("gitlab");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "120"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://gitlab.com/api/v4/projects/77/pipelines/5000/retry,
                  POST https://gitlab.com/api/v4/projects/77/pipelines/5001/cancel
                ]
                """);
    }

    [Test]
    public async Task FetchLogLeavesOutJobsAllowedToFail()
    {
        var handler = Handler()
            .Get(
                "https://gitlab.com/api/v4/projects/77/pipelines/5000/jobs?scope[]=failed&per_page=100",
                """
                [
                  {"id":903,"name":"lint","stage":"test","status":"failed","allow_failure":true},
                  {"id":902,"name":"unit","stage":"test","status":"failed","allow_failure":false}
                ]
                """)
            .Get("https://gitlab.com/api/v4/projects/77/jobs/902/trace", "1 test failed\n");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("gitlab").FetchLog(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> test / unit <==\n1 test failed");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://gitlab.com/api/v4/projects/77/pipelines/5000/jobs?scope[]=failed&per_page=100",
            "GET https://gitlab.com/api/v4/projects/77/jobs/902/trace"
        ]);
    }

    /// <summary>
    /// The archives come from the same job listing the log reads, so a pipeline's artifacts cost
    /// one request and their sizes come free with it.
    /// </summary>
    [Test]
    public async Task ListArtifactsComesFromTheJobListing()
    {
        var handler = Handler()
            .Get(
                "https://gitlab.com/api/v4/projects/77/pipelines/5000/jobs?scope[]=failed&per_page=100",
                """
                [
                  {"id":903,"name":"lint","stage":"test","allow_failure":true,"artifacts_file":{"filename":"artifacts.zip","size":11}},
                  {"id":902,"name":"unit","stage":"test","allow_failure":false,"artifacts_file":{"filename":"artifacts.zip","size":2048}},
                  {"id":904,"name":"old","stage":"test","allow_failure":false,"artifacts_file":{"filename":"artifacts.zip","size":99},"artifacts_expire_at":"2020-01-01T00:00:00Z"},
                  {"id":905,"name":"nothing","stage":"test","allow_failure":false}
                ]
                """);
        var context = ProviderTestHelpers.Context("gitlab", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        handler.Requests.Clear();
        var artifacts = await ProviderTestHelpers.Provider("gitlab").ListArtifacts(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
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
                      Id: 902,
                      Name: unit-artifacts.zip,
                      Bytes: 2048
                    },
                    {
                      Id: 904,
                      Name: old-artifacts.zip,
                      Bytes: 99,
                      Unavailable: expired
                    }
                  ],
                  Requests: [
                    GET https://gitlab.com/api/v4/projects/77/pipelines/5000/jobs?scope[]=failed&per_page=100
                  ]
                }
                """);
    }

    [Test]
    public async Task DownloadAnArtifact()
    {
        var handler = Handler()
            .MapBytes("GET", "https://gitlab.com/api/v4/projects/77/jobs/902/artifacts", [80, 75, 5, 6]);
        var context = ProviderTestHelpers.Context("gitlab", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        handler.Requests.Clear();
        using var destination = new MemoryStream();
        var written = await ProviderTestHelpers.Provider("gitlab").DownloadArtifact(
            context,
            builds.Single(_ => _.RunNumber == "119"),
            new("902", "unit-artifacts.zip", 4),
            destination,
            ArtifactPlan.DefaultPerFile,
            Cancel.None);
        await Assert.That(written).IsEqualTo(4);
        await Assert.That(handler.Requests.Single()).IsEqualTo("GET https://gitlab.com/api/v4/projects/77/jobs/902/artifacts");
    }

    [Test]
    public async Task GroupScopeAndSelfHosted()
    {
        var handler = new FakeHttpHandler()
            .Get("https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&min_access_level=20&simple=true&archived=false&order_by=last_activity_at&per_page=100", "[]")
            .Get("https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&min_access_level=30&simple=true&archived=false&order_by=last_activity_at&per_page=100", "[]");
        var context = ProviderTestHelpers.Context("gitlab", handler, "https://gitlab.example.com/", scope: ("group", "verify"));
        await ProviderTestHelpers.Provider("gitlab").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&min_access_level=20&simple=true&archived=false&order_by=last_activity_at&per_page=100,
                  GET https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&min_access_level=30&simple=true&archived=false&order_by=last_activity_at&per_page=100
                ]
                """);
    }

    [Test]
    public async Task TokenGoesInThePrivateTokenHeader()
    {
        var handler = new FakeHttpHandler().Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.Provider("gitlab").Test(context, Cancel.None);
        // The test's own requests and those asking what the token may do alike.
        await Assert.That(handler.RequestHeaders.Select(_ => _.GetValues("PRIVATE-TOKEN").Single()).Distinct()).IsEquivalentTo(["secret"]);
    }

    [Test]
    [Arguments(AuthMethod.Browser)]
    [Arguments(AuthMethod.Device)]
    public async Task ASignInsTokenGoesInTheAuthorizationHeader(AuthMethod method)
    {
        // GitLab finds an OAuth token only as a Bearer token, and looks for an access token in
        // PRIVATE-TOKEN, so a sign in's token there was refused on every request.
        var handler = new FakeHttpHandler().Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""");
        var context = ProviderTestHelpers.Context("gitlab", handler, auth: method);
        await ProviderTestHelpers.Provider("gitlab").Test(context, Cancel.None);
        await Assert.That(handler.RequestHeaders.Select(_ => _.Authorization?.ToString() ?? "none").Distinct()).IsEquivalentTo(["Bearer secret"]);
        await Assert.That(handler.RequestHeaders.Any(_ => _.Contains("PRIVATE-TOKEN"))).IsFalse();
    }

    static FakeHttpHandler Token(string token) =>
        new FakeHttpHandler()
            .Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""")
            .Get("https://gitlab.com/api/v4/personal_access_tokens/self", token);

    [Test]
    [Arguments("""{"scopes":["api","read_user"],"granular":false}""", nameof(BuildAccess.Change))]
    [Arguments("""{"scopes":["read_api"]}""", nameof(BuildAccess.Watch))]
    [Arguments("""{"scopes":["read_user"]}""", nameof(BuildAccess.Unknown))]
    [Arguments("""{"scopes":[],"granular":true}""", nameof(BuildAccess.Unknown))]
    public async Task AnAccessTokenIsJudgedByItsScopes(string token, string expected)
    {
        var context = ProviderTestHelpers.Context("gitlab", Token(token));
        var access = await ProviderTestHelpers.Provider("gitlab").Access(context, Cancel.None);
        await Assert.That(access.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"resource_owner_id":1,"scope":["read_api","api"],"expires_in":7200,"application":{"uid":"app"},"created_at":1767268800}""", nameof(BuildAccess.Change))]
    [Arguments("""{"resource_owner_id":1,"scope":["read_api"],"expires_in":7200,"application":{"uid":"app"},"created_at":1767268800}""", nameof(BuildAccess.Watch))]
    [Arguments("""{"resource_owner_id":1,"scope":["read_user"],"expires_in":7200,"application":{"uid":"app"},"created_at":1767268800}""", nameof(BuildAccess.Unknown))]
    public async Task ASignInsTokenIsJudgedByItsTokenInfo(string info, string expected)
    {
        // personal_access_tokens/self refuses an OAuth token.
        var handler = new FakeHttpHandler()
            .Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""")
            .Get("https://gitlab.com/oauth/token/info", info);
        var context = ProviderTestHelpers.Context("gitlab", handler, auth: AuthMethod.Device);
        var access = await ProviderTestHelpers.Provider("gitlab").Access(context, Cancel.None);
        await Assert.That(access.ToString()).IsEqualTo(expected);
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://gitlab.com/api/v4/user",
            "GET https://gitlab.com/oauth/token/info"
        ]);
    }

    [Test]
    public async Task TokenInfoIsAskedForUnderTheServersPath()
    {
        // GitLab served under a relative URL root serves its OAuth routes under it too.
        var handler = new FakeHttpHandler()
            .Get("https://example.com/gitlab/api/v4/user", """{"username":"simon"}""")
            .Get("https://example.com/gitlab/oauth/token/info", """{"scope":["api"]}""");
        var context = ProviderTestHelpers.Context("gitlab", handler, "https://example.com/gitlab", auth: AuthMethod.Browser);
        var access = await ProviderTestHelpers.Provider("gitlab").Access(context, Cancel.None);
        await Assert.That(access).IsEqualTo(BuildAccess.Change);
    }

    [Test]
    public async Task TheTestSaysASignInCanChangeBuilds()
    {
        var handler = new FakeHttpHandler()
            .Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""")
            .Get("https://gitlab.com/oauth/token/info", """{"scope":["read_api","api"]}""");
        var context = ProviderTestHelpers.Context("gitlab", handler, auth: AuthMethod.Device);
        var result = await ProviderTestHelpers.Provider("gitlab").Test(context, Cancel.None);
        await Assert.That(result.Describe(ProviderDescriptors.GitLab)).IsEqualTo("Signed in as simon. The connection can watch and change builds");
    }

    [Test]
    public async Task AGitLabWithoutTheTokenRouteSaysNothing()
    {
        var handler = new FakeHttpHandler().Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""");
        var access = await ProviderTestHelpers.Provider("gitlab").Access(ProviderTestHelpers.Context("gitlab", handler), Cancel.None);
        await Assert.That(access).IsEqualTo(BuildAccess.Unknown);
    }

    [Test]
    public async Task TheTestSaysATokenCanOnlyWatch()
    {
        var context = ProviderTestHelpers.Context("gitlab", Token("""{"scopes":["read_api"]}"""));
        var result = await ProviderTestHelpers.Provider("gitlab").Test(context, Cancel.None);
        await Assert.That(result.Describe(ProviderDescriptors.GitLab))
            .IsEqualTo("Signed in as simon. The connection can watch builds but not change them. GitLab CI needs the api scope");
    }

    [Test]
    public async Task AProjectWhereTheUserIsOnlyAReporterOffersNoRetryOrCancel()
    {
        var handler = Handler().Get(developerListing, "[]");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Assert.That(builds.Count).IsEqualTo(3);
        await Assert.That(builds.Any(_ => _.CanRetry || _.CanCancel)).IsFalse();
    }

    [Test]
    public async Task AFullPageAtDeveloperTakesNothingAway()
    {
        // Activity between the two listings can push a project the first has off a full page.
        var others = string.Join(',', Enumerable.Range(1000, 100).Select(_ => $$"""{"id":{{_}},"path_with_namespace":"verify/p{{_}}","web_url":"https://gitlab.com/verify/p{{_}}"}"""));
        var handler = Handler().Get(developerListing, $"[{others}]");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", ProviderTestHelpers.Context("gitlab", handler));
        await Assert.That(builds.Single(_ => _.RunNumber == "119").CanRetry).IsTrue();
        await Assert.That(builds.Single(_ => _.RunNumber == "120").CanCancel).IsTrue();
    }

    [Test]
    public async Task AnAdministratorIsNotNarrowedByRole()
    {
        var handler = Handler()
            .Get("https://gitlab.com/api/v4/user", """{"username":"root","is_admin":true}""")
            .Get(developerListing, "[]");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.Provider("gitlab").Access(context, Cancel.None);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        await Assert.That(builds.Single(_ => _.RunNumber == "119").CanRetry).IsTrue();
        await Assert.That(handler.Requests).DoesNotContain($"GET {developerListing}");
    }

    [Test]
    public async Task AConnectionThatCanOnlyWatchIsNotListedAtDeveloper()
    {
        var handler = Handler();
        var context = ProviderTestHelpers.Context("gitlab", handler)
            with
            {
                Access = BuildAccess.Watch
            };
        await ProviderTestHelpers.Provider("gitlab").DiscoverPipelines(context, Cancel.None);
        await Assert.That(handler.Requests).DoesNotContain($"GET {developerListing}");
    }

    [Test]
    public async Task AFailedListingAtDeveloperKeepsWhatTheLastOneFound()
    {
        var handler = Handler().Get(developerListing, "[]");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        handler.Map("GET", developerListing, "boom", HttpStatusCode.InternalServerError);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gitlab", context);
        await Assert.That(builds.Single(_ => _.RunNumber == "119").CanRetry).IsFalse();
    }
}
