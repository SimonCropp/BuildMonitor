public class GitLabProviderTests
{
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://gitlab.com/api/v4/projects?membership=true&simple=true&archived=false&order_by=last_activity_at&per_page=100",
                """[{"id":77,"path_with_namespace":"verify/diffengine","web_url":"https://gitlab.com/verify/diffengine"}]""")
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
    public async Task GroupScopeAndSelfHosted()
    {
        var handler = new FakeHttpHandler()
            .Get("https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&simple=true&archived=false&order_by=last_activity_at&per_page=100", "[]");
        var context = ProviderTestHelpers.Context("gitlab", handler, "https://gitlab.example.com/", scope: ("group", "verify"));
        await ProviderTestHelpers.Provider("gitlab").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://gitlab.example.com/api/v4/groups/verify/projects?include_subgroups=true&simple=true&archived=false&order_by=last_activity_at&per_page=100
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
