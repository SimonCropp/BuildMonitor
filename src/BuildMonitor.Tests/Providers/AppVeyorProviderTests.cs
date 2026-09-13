public class AppVeyorProviderTests
{
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://ci.appveyor.com/api/projects",
                """
                [{"projectId":1,"accountName":"simon","slug":"diffengine","name":"DiffEngine","repositoryType":"gitHub","repositoryName":"VerifyTests/DiffEngine"}]
                """)
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5",
                """
                {"project":{"slug":"diffengine"},"builds":[
                  {"buildId":100,"buildNumber":45,"version":"1.0.45","branch":"main","commitId":"abc123","message":"Fix","authorName":"Simon","status":"running","created":"2026-01-01T11:50:00+00:00","started":"2026-01-01T11:51:00+00:00"},
                  {"buildId":99,"buildNumber":44,"version":"1.0.44","branch":"feature","commitId":"def456","message":"Feature","authorName":"Simon","pullRequestId":"12","status":"failed","created":"2026-01-01T10:50:00+00:00","started":"2026-01-01T10:51:00+00:00","finished":"2026-01-01T10:55:00+00:00"}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", ProviderTestHelpers.Context("appveyor", handler));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("PUT", "https://ci.appveyor.com/api/builds", "{}")
            .Map("DELETE", "https://ci.appveyor.com/api/builds/simon/diffengine/1.0.45", "", HttpStatusCode.NoContent);
        var context = ProviderTestHelpers.Context("appveyor", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("appveyor");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "44"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "45"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  PUT https://ci.appveyor.com/api/builds
                  {"buildId":99,"reRunIncomplete":false},
                  DELETE https://ci.appveyor.com/api/builds/simon/diffengine/1.0.45
                ]
                """);
    }

    [Test]
    public async Task UserLevelTokenNamesTheAccount()
    {
        var handler = new FakeHttpHandler()
            .Get("https://ci.appveyor.com/api/account/simon/projects", "[]");
        var context = ProviderTestHelpers.Context("appveyor", handler, scope: ("account", "simon"));
        await ProviderTestHelpers.Provider("appveyor").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://ci.appveyor.com/api/account/simon/projects
                ]
                """);
    }
}
