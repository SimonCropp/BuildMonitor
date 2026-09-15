public class AppVeyorProviderTests
{
    static FakeHttpHandler Handler(string projectsUrl = "https://ci.appveyor.com/api/projects") =>
        new FakeHttpHandler()
            .Get(
                projectsUrl,
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
    public async Task RecentActivityTakesEachProjectsLatestBuild()
    {
        var handler = new FakeHttpHandler()
            .Get(
                "https://ci.appveyor.com/api/projects",
                """
                [
                  {"accountName":"simon","slug":"diffengine","name":"DiffEngine","builds":[{"buildId":100,"buildNumber":45,"version":"1.0.45","status":"running","updated":"2026-01-01T11:51:30+00:00"}]},
                  {"accountName":"simon","slug":"unbuilt","name":"Unbuilt","builds":[]}
                ]
                """);
        var context = ProviderTestHelpers.Context("appveyor", handler);
        var activity = await ProviderTestHelpers.Provider("appveyor").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!.Count).IsEqualTo(1);
        await Assert.That(activity["simon/diffengine"]).IsEqualTo("100|running|2026-01-01T11:51:30.0000000+00:00");
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
    public async Task FetchLogOfTheFailedJobs()
    {
        var handler = Handler()
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/build/1.0.44",
                """
                {"project":{"slug":"diffengine"},"build":{"buildId":99,"buildNumber":44,"version":"1.0.44","status":"failed","jobs":[
                  {"jobId":"a1b2","name":"","status":"failed"},
                  {"jobId":"c3d4","name":"Environment: docs","status":"success"}
                ]}}
                """)
            .Get("https://ci.appveyor.com/api/buildjobs/a1b2/log", "Build FAILED.\n");
        var context = ProviderTestHelpers.Context("appveyor", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("appveyor").FetchLog(context, builds.Single(_ => _.RunNumber == "44"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> a1b2 <==\nBuild FAILED.");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://ci.appveyor.com/api/projects/simon/diffengine/build/1.0.44",
            "GET https://ci.appveyor.com/api/buildjobs/a1b2/log"
        ]);
    }

    [Test]
    public async Task UserLevelTokenPrefixesOnlyCallsThatDoNotNameTheAccount()
    {
        var handler = Handler("https://ci.appveyor.com/api/account/simon/projects")
            .Map("PUT", "https://ci.appveyor.com/api/account/simon/builds", "{}")
            .Map("DELETE", "https://ci.appveyor.com/api/builds/simon/diffengine/1.0.45", "", HttpStatusCode.NoContent);
        var context = ProviderTestHelpers.Context("appveyor", handler, scope: ("account", "simon"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        var provider = ProviderTestHelpers.Provider("appveyor");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "44"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "45"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://ci.appveyor.com/api/account/simon/projects,
                  GET https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5,
                  PUT https://ci.appveyor.com/api/account/simon/builds
                  {"buildId":99,"reRunIncomplete":false},
                  DELETE https://ci.appveyor.com/api/builds/simon/diffengine/1.0.45
                ]
                """);
    }
}
