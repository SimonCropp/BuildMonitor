public class TravisProviderTests
{
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://api.travis-ci.com/repos?repository.active=true&limit=100&sort_by=default_branch.last_build:desc",
                """
                {"repositories":[{"id":1,"slug":"VerifyTests/DiffEngine","default_branch":{"name":"main"}}]}
                """)
            .Get(
                "https://api.travis-ci.com/repo/VerifyTests%2FDiffEngine/builds?limit=5&sort_by=id:desc&include=build.commit",
                """
                {"builds":[
                  {"id":900,"number":"120","state":"started","started_at":"2026-01-01T11:55:00Z","finished_at":null,"pull_request_number":null,"branch":{"name":"main"},"commit":{"sha":"abc","message":"Fix","author":{"name":"Simon"}}},
                  {"id":899,"number":"119","state":"failed","started_at":"2026-01-01T10:00:00Z","finished_at":"2026-01-01T10:10:00Z","pull_request_number":7,"branch":{"name":"feature"},"commit":{"sha":"def","message":"Feature","author":{"name":"Simon"}}}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("travis", ProviderTestHelpers.Context("travis", handler));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task RecentActivityReadsTheLastStartedBuildOfEachRepository()
    {
        const string url = "https://api.travis-ci.com/repos?repository.active=true&limit=100&sort_by=current_build:desc&include=repository.last_started_build";
        var handler = new FakeHttpHandler()
            .Get(url, """{"repositories":[{"id":1,"slug":"VerifyTests/DiffEngine","last_started_build":{"id":900,"number":"120","state":"started"}},{"id":2,"slug":"VerifyTests/Empty","last_started_build":null}]}""");
        var context = ProviderTestHelpers.Context("travis", handler);
        var activity = await ProviderTestHelpers.Provider("travis").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!.Count).IsEqualTo(1);
        await Assert.That(activity["VerifyTests/DiffEngine"]).IsEqualTo("900|started");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {url}");
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("POST", "https://api.travis-ci.com/build/899/restart", "{}", HttpStatusCode.Accepted)
            .Map("POST", "https://api.travis-ci.com/build/900/cancel", "{}", HttpStatusCode.Accepted);
        var context = ProviderTestHelpers.Context("travis", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("travis", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("travis");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "120"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://api.travis-ci.com/build/899/restart,
                  POST https://api.travis-ci.com/build/900/cancel
                ]
                """);
    }

    [Test]
    public async Task FetchLogOfTheFailedJobsNotAllowedToFail()
    {
        var handler = Handler()
            .Get(
                "https://api.travis-ci.com/build/899/jobs",
                """{"jobs":[{"id":5001,"number":"119.1","state":"passed","allow_failure":false},{"id":5002,"number":"119.2","state":"failed","allow_failure":false},{"id":5003,"number":"119.3","state":"errored","allow_failure":true}]}""")
            .Get("https://api.travis-ci.com/job/5002/log.txt", "The command \"npm test\" exited with 1.\n");
        var context = ProviderTestHelpers.Context("travis", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("travis", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("travis").FetchLog(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> Job 119.2 <==\nThe command \"npm test\" exited with 1.");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://api.travis-ci.com/build/899/jobs",
            "GET https://api.travis-ci.com/job/5002/log.txt"
        ]);
    }

    [Test]
    public async Task SendsTheVersionHeaderAndTokenScheme()
    {
        var handler = new FakeHttpHandler().Get("https://api.travis-ci.com/user", """{"login":"simon"}""");
        var context = ProviderTestHelpers.Context("travis", handler);
        await ProviderTestHelpers.Provider("travis").Test(context, Cancel.None);
        var headers = handler.RequestHeaders.Single();
        await Assert.That(headers.GetValues("Travis-API-Version").Single()).IsEqualTo("3");
        await Assert.That(headers.Authorization!.ToString()).IsEqualTo("token secret");
    }

    [Test]
    public async Task ABuildsPermissionsDecideRetryAndCancel()
    {
        // They are the checks a restart or a cancel of the build meets.
        var handler = Handler()
            .Get(
                "https://api.travis-ci.com/repo/VerifyTests%2FDiffEngine/builds?limit=5&sort_by=id:desc&include=build.commit",
                """
                {"builds":[
                  {"id":900,"number":"120","state":"started","branch":{"name":"main"},"@permissions":{"read":true,"cancel":false,"restart":false,"prioritize":false}},
                  {"id":899,"number":"119","state":"failed","branch":{"name":"feature"},"@permissions":{"read":true,"cancel":true,"restart":true,"prioritize":false}},
                  {"id":898,"number":"118","state":"errored","branch":{"name":"other"},"@permissions":{"read":true,"cancel":true,"restart":false,"prioritize":false}}
                ]}
                """);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("travis", ProviderTestHelpers.Context("travis", handler));
        await Assert.That(builds.Single(_ => _.RunNumber == "120").CanCancel).IsFalse();
        await Assert.That(builds.Single(_ => _.RunNumber == "119").CanRetry).IsTrue();
        await Assert.That(builds.Single(_ => _.RunNumber == "118").CanRetry).IsFalse();
    }
}
