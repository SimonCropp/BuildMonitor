public class GitLabProviderTests
{
    const string graph = "https://gitlab.com/api/graphql";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://gitlab.com/api/v4/projects?membership=true&min_access_level=20&simple=true&archived=false&order_by=last_activity_at&per_page=100",
                """[{"id":77,"path_with_namespace":"verify/diffengine","web_url":"https://gitlab.com/verify/diffengine"}]""")
            .Get(
                graph,
                """
                {"data":{"projects":{"nodes":[{"id":"gid://gitlab/Project/77","pipelines":{"nodes":[
                  {"id":"gid://gitlab/Ci::Pipeline/5001","iid":"120","status":"RUNNING","ref":"main","sha":"abc123","createdAt":"2026-01-01T11:55:00Z","updatedAt":"2026-01-01T11:56:00Z","startedAt":"2026-01-01T11:55:30Z","finishedAt":null,"user":{"name":"Simon"}},
                  {"id":"gid://gitlab/Ci::Pipeline/5000","iid":"119","status":"FAILED","ref":"refs/merge-requests/9/head","sha":"def456","createdAt":"2026-01-01T10:00:00Z","updatedAt":"2026-01-01T10:08:00Z","startedAt":"2026-01-01T10:00:20Z","finishedAt":"2026-01-01T10:08:00Z","user":{"name":"Simon"}}
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
        await Verify(new { builds, handler.Requests });
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

    [Test]
    public async Task GroupScopeAndSelfHosted()
    {
        var handler = new FakeHttpHandler()
            .Get("https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&min_access_level=20&simple=true&archived=false&order_by=last_activity_at&per_page=100", "[]");
        var context = ProviderTestHelpers.Context("gitlab", handler, "https://gitlab.example.com/", scope: ("group", "verify"));
        await ProviderTestHelpers.Provider("gitlab").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&min_access_level=20&simple=true&archived=false&order_by=last_activity_at&per_page=100
                ]
                """);
    }

    [Test]
    public async Task TokenGoesInThePrivateTokenHeader()
    {
        var handler = new FakeHttpHandler().Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""");
        var context = ProviderTestHelpers.Context("gitlab", handler);
        await ProviderTestHelpers.Provider("gitlab").Test(context, Cancel.None);
        await Assert.That(handler.RequestHeaders.Single().GetValues("PRIVATE-TOKEN").Single()).IsEqualTo("secret");
    }
}
