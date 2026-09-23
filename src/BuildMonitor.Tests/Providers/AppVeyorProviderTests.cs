public class AppVeyorProviderTests
{
    static FakeHttpHandler Handler(string projectsUrl = "https://ci.appveyor.com/api/projects") =>
        new FakeHttpHandler()
            .Get(
                projectsUrl,
                """
                [{"projectId":1,"accountName":"simon","slug":"diffengine","name":"DiffEngine","repositoryType":"gitHub","repositoryName":"VerifyTests/DiffEngine","repositoryBranch":"main"}]
                """)
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5",
                """
                {"project":{"slug":"diffengine"},"builds":[
                  {"buildId":101,"buildNumber":46,"version":"1.0.46","branch":"main","commitId":"413544f","message":"Use main","authorName":"Someone","pullRequestId":"13","pullRequestHeadBranch":"main","pullRequestHeadRepository":"someone/DiffEngine","status":"queued","created":"2026-01-01T11:55:00+00:00"},
                  {"buildId":100,"buildNumber":45,"version":"1.0.45","branch":"main","commitId":"abc123","message":"Fix","authorName":"Simon","status":"running","created":"2026-01-01T11:50:00+00:00","started":"2026-01-01T11:51:00+00:00"},
                  {"buildId":99,"buildNumber":44,"version":"1.0.44","branch":"main","commitId":"def456","message":"Feature","authorName":"Simon","pullRequestId":"12","pullRequestHeadBranch":"feature","pullRequestHeadRepository":"VerifyTests/DiffEngine","status":"failed","created":"2026-01-01T10:50:00+00:00","started":"2026-01-01T10:51:00+00:00","finished":"2026-01-01T10:55:00+00:00"}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", ProviderTestHelpers.Context("appveyor", handler));
        await Verify(new { builds, handler.Requests });
    }

    /// <summary>
    /// A project whose setting still says master, as one added before its repository moved to main
    /// does, and whose history is all pull requests: each builds its branch and then the pull request,
    /// which targets main. The pull requests say which branch is the project's, and its newest
    /// build is asked for by branch, since the history left it out.
    /// </summary>
    [Test]
    public async Task PullRequestsFillingTheHistoryFetchTheDefaultBranchsNewestBuild()
    {
        var handler = StaleSetting()
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5",
                """
                {"project":{"slug":"diffengine"},"builds":[
                  {"buildId":103,"buildNumber":48,"version":"1.0.48","branch":"main","pullRequestId":"15","pullRequestHeadBranch":"fix-b","pullRequestHeadRepository":"VerifyTests/DiffEngine","status":"success","created":"2026-01-01T11:55:00+00:00","started":"2026-01-01T11:55:30+00:00","finished":"2026-01-01T11:58:00+00:00"},
                  {"buildId":102,"buildNumber":47,"version":"1.0.47","branch":"main","pullRequestId":"14","pullRequestHeadBranch":"fix-a","pullRequestHeadRepository":"VerifyTests/DiffEngine","status":"success","created":"2026-01-01T11:50:00+00:00","started":"2026-01-01T11:50:30+00:00","finished":"2026-01-01T11:53:00+00:00"},
                  {"buildId":101,"buildNumber":46,"version":"1.0.46","branch":"fix-a","status":"success","created":"2026-01-01T11:45:00+00:00","started":"2026-01-01T11:45:30+00:00","finished":"2026-01-01T11:48:00+00:00"}
                ]}
                """)
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/branch/main",
                """
                {"project":{"slug":"diffengine"},"build":{"buildId":100,"buildNumber":45,"version":"1.0.45","branch":"main","status":"success","created":"2026-01-01T11:00:00+00:00","started":"2026-01-01T11:00:30+00:00","finished":"2026-01-01T11:05:00+00:00"}}
                """);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", ProviderTestHelpers.Context("appveyor", handler));
        await Verify(
                new
                {
                    builds = builds.Select(_ => $"#{_.RunNumber} {_.Branch} (default {_.DefaultBranch})"),
                    handler.Requests
                })
            .Snapshot(
                """
                {
                  builds: [
                    #48 fix-b (default main),
                    #47 fix-a (default main),
                    #46 fix-a (default main),
                    #45 main (default main)
                  ],
                  Requests: [
                    GET https://ci.appveyor.com/api/projects,
                    GET https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5,
                    GET https://ci.appveyor.com/api/projects/simon/diffengine/branch/main
                  ]
                }
                """);
    }

    /// <summary>
    /// With no pull requests to say otherwise the setting is tried, and master's newest build is
    /// from before the history cutoff: the branch nothing builds any more. It is not the pipeline's
    /// for the next hour, so the fetches in it ask for nothing but the history.
    /// </summary>
    [Test]
    public async Task ASettingWithNothingBuiltOnItLatelyIsPassedOver()
    {
        var handler = StaleSetting()
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5",
                """
                {"project":{"slug":"diffengine"},"builds":[
                  {"buildId":101,"buildNumber":46,"version":"1.0.46","branch":"main","status":"success","created":"2026-01-01T11:45:00+00:00","started":"2026-01-01T11:45:30+00:00","finished":"2026-01-01T11:48:00+00:00"}
                ]}
                """)
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/branch/master",
                """
                {"project":{"slug":"diffengine"},"build":{"buildId":7,"buildNumber":3,"version":"1.0.3","branch":"master","status":"success","created":"2023-05-01T11:00:00+00:00","started":"2023-05-01T11:00:30+00:00","finished":"2023-05-01T11:05:00+00:00"}}
                """);
        var context = ProviderTestHelpers.Context("appveyor", handler) with
        {
            Since = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var first = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        var second = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        await Verify(
                new
                {
                    first = first.Select(_ => $"#{_.RunNumber} {_.Branch} (default {_.DefaultBranch})"),
                    second = second.Select(_ => $"#{_.RunNumber} {_.Branch} (default {_.DefaultBranch})"),
                    handler.Requests
                })
            .Snapshot(
                """
                {
                  first: [
                    #46 main (default master)
                  ],
                  second: [
                    #46 main (default )
                  ],
                  Requests: [
                    GET https://ci.appveyor.com/api/projects,
                    GET https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5,
                    GET https://ci.appveyor.com/api/projects/simon/diffengine/branch/master,
                    GET https://ci.appveyor.com/api/projects,
                    GET https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5
                  ]
                }
                """);
    }

    /// <summary>
    /// A default branch with no build at all answers 404, which is an answer rather than a failure:
    /// the fetch keeps its builds, and the next one within the hour does not ask again.
    /// </summary>
    [Test]
    public async Task ADefaultBranchWithNoBuildIsNotAskedAgainForAnHour()
    {
        var handler = new FakeHttpHandler()
            .Get(
                "https://ci.appveyor.com/api/projects",
                """
                [{"projectId":1,"accountName":"simon","slug":"diffengine","name":"DiffEngine","repositoryType":"gitHub","repositoryName":"VerifyTests/DiffEngine","repositoryBranch":"main"}]
                """)
            .Get(
                "https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5",
                """
                {"project":{"slug":"diffengine"},"builds":[
                  {"buildId":102,"buildNumber":47,"version":"1.0.47","branch":"main","pullRequestId":"14","pullRequestHeadBranch":"fix-a","pullRequestHeadRepository":"VerifyTests/DiffEngine","status":"running","created":"2026-01-01T11:50:00+00:00","started":"2026-01-01T11:50:30+00:00"}
                ]}
                """);
        var context = ProviderTestHelpers.Context("appveyor", handler);
        var first = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        await Assert.That(first.Single().DefaultBranch).IsEqualTo("main");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://ci.appveyor.com/api/projects",
            "GET https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5",
            "GET https://ci.appveyor.com/api/projects/simon/diffengine/branch/main",
            "GET https://ci.appveyor.com/api/projects",
            "GET https://ci.appveyor.com/api/projects/simon/diffengine/history?recordsNumber=5"
        ]);
    }

    static FakeHttpHandler StaleSetting() =>
        new FakeHttpHandler()
            .Get(
                "https://ci.appveyor.com/api/projects",
                """
                [{"projectId":1,"accountName":"simon","slug":"diffengine","name":"DiffEngine","repositoryType":"gitHub","repositoryName":"VerifyTests/DiffEngine","repositoryBranch":"master"}]
                """);

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

    /// <summary>
    /// Every job's artifacts, not only the failed one's: in a matrix the leg that broke often
    /// published nothing while a sibling holds the report that says why.
    /// </summary>
    [Test]
    public async Task ListArtifactsCoversEveryJob()
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
            .Get("https://ci.appveyor.com/api/buildjobs/a1b2/artifacts", "[]")
            .Get("https://ci.appveyor.com/api/buildjobs/c3d4/artifacts", """[{"fileName":"results.trx","name":"results","type":"Auto","size":2048}]""");
        var context = ProviderTestHelpers.Context("appveyor", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        handler.Requests.Clear();
        var artifacts = await ProviderTestHelpers.Provider("appveyor").ListArtifacts(context, builds.Single(_ => _.RunNumber == "44"), Cancel.None);
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
                      Id: c3d4|results.trx,
                      Name: results.trx,
                      Bytes: 2048
                    }
                  ],
                  Requests: [
                    GET https://ci.appveyor.com/api/projects/simon/diffengine/build/1.0.44,
                    GET https://ci.appveyor.com/api/buildjobs/a1b2/artifacts,
                    GET https://ci.appveyor.com/api/buildjobs/c3d4/artifacts
                  ]
                }
                """);
    }

    [Test]
    public async Task DownloadAnArtifactTakesTheJobFromItsId()
    {
        var handler = Handler()
            .MapBytes("GET", "https://ci.appveyor.com/api/buildjobs/c3d4/artifacts/results.trx", [1, 2, 3]);
        var context = ProviderTestHelpers.Context("appveyor", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("appveyor", context);
        handler.Requests.Clear();
        using var destination = new MemoryStream();
        var written = await ProviderTestHelpers.Provider("appveyor").DownloadArtifact(
            context,
            builds.Single(_ => _.RunNumber == "44"),
            new("c3d4|results.trx", "results.trx", 3),
            destination,
            ArtifactPlan.DefaultPerFile,
            Cancel.None);
        await Assert.That(written).IsEqualTo(3);
        await Assert.That(handler.Requests.Single()).IsEqualTo("GET https://ci.appveyor.com/api/buildjobs/c3d4/artifacts/results.trx");
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
