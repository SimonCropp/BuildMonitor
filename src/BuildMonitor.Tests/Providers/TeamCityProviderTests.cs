public class TeamCityProviderTests
{
    const string server = "https://teamcity.example.com";

    // Discovery and fetch both read buildTypes, so discovery is mapped by its full URL and the
    // fetch falls back to the URL without its query.
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{server}/app/rest/buildTypes?locator=affectedProject:(id:_Root)&fields=buildType(id,name,projectName,projectId,webUrl)",
                """
                {"buildType":[
                  {"id":"Verify_Build","name":"Build","projectName":"Verify","projectId":"Verify","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build"},
                  {"id":"Verify_Docs","name":"Docs","projectName":"Verify","projectId":"Verify","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Docs"}
                ]}
                """)
            .Get(
                $"{server}/app/rest/buildTypes",
                """
                {"buildType":[
                  {"id":"Verify_Build","builds":{"build":[
                    {"id":9001,"number":"120","status":"SUCCESS","state":"running","branchName":"pull/15","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9001","statusText":"Step 2 of 4","queuedDate":"20260101T115000+0000","startDate":"20260101T115100+0000","buildTypeId":"Verify_Build","running-info":{"percentageComplete":40,"elapsedSeconds":120,"estimatedTotalSeconds":300,"leftSeconds":180},"triggered":{"user":{"username":"simon","name":"Simon"}},"revisions":{"revision":[{"version":"abc123"}]}},
                    {"id":9000,"number":"119","status":"FAILURE","state":"finished","branchName":"main","defaultBranch":true,"webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9000","statusText":"Tests failed: 1","queuedDate":"20260101T100000+0000","startDate":"20260101T100100+0000","finishDate":"20260101T100600+0000","buildTypeId":"Verify_Build"}
                  ]}},
                  {"id":"Verify_Docs","builds":{"build":[
                    {"id":8999,"number":"50","status":"UNKNOWN","state":"finished","branchName":"main","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Docs/8999","statusText":"Canceled","queuedDate":"20260101T090000+0000","startDate":"20260101T090100+0000","finishDate":"20260101T090200+0000","buildTypeId":"Verify_Docs","canceledInfo":{"text":"stopped"}}
                  ]}},
                  {"id":"Other_Config","builds":{"build":[
                    {"id":8998,"number":"3","status":"SUCCESS","state":"queued","webUrl":"https://teamcity.example.com/buildConfiguration/Other/8998","queuedDate":"20260101T115900+0000","buildTypeId":"Other_Config"}
                  ]}}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("teamcity", ProviderTestHelpers.Context("teamcity", handler, server));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task HistoryLimitIsNotSentAsQueuedDate()
    {
        // A queuedDate filter would hide a build queued before the cutoff that is still queued or running.
        var handler = Handler();
        var context = ProviderTestHelpers.Context("teamcity", handler, server) with { Since = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero) };
        await ProviderTestHelpers.DiscoverAndFetch("teamcity", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("count:5)"))).IsTrue();
        await Assert.That(handler.Requests.Any(_ => _.Contains("queuedDate:("))).IsFalse();
    }

    [Test]
    public async Task AQueueDoesNotHideAQuietConfiguration()
    {
        var queued = string.Join(',', Enumerable.Range(1, 5).Select(_ => $$"""{"id":{{_}},"state":"queued","buildTypeId":"Verify_Build","queuedDate":"20260101T115900+0000"}"""));
        var handler = new FakeHttpHandler()
            .Get(
                $"{server}/app/rest/buildTypes",
                $$$"""
                {"buildType":[
                  {"id":"Verify_Build","builds":{"build":[{{{queued}}}]}},
                  {"id":"Verify_Docs","builds":{"build":[{"id":100,"number":"7","status":"SUCCESS","state":"finished","buildTypeId":"Verify_Docs","queuedDate":"20240101T100000+0000","startDate":"20240101T100100+0000","finishDate":"20240101T100500+0000"}]}}
                ]}
                """);
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        Pipeline[] pipelines =
        [
            new("Verify_Build", "Verify / Build", "Verify", "Verify", $"{server}/buildConfiguration/Verify_Build"),
            new("Verify_Docs", "Verify / Docs", "Verify", "Verify", $"{server}/buildConfiguration/Verify_Docs")
        ];
        var builds = await ProviderTestHelpers.Provider("teamcity").FetchBuilds(context, pipelines, 5, Cancel.None);
        await Assert.That(builds.Count(_ => _.PipelineId == "Verify_Build")).IsEqualTo(5);
        await Assert.That(builds.Single(_ => _.PipelineId == "Verify_Docs").RunNumber).IsEqualTo("7");
    }

    [Test]
    public async Task EachProjectIsFetchedByItself()
    {
        // The whole server in one request, while anything on it ran, was about a megabyte a poll on
        // five hundred configurations, most of them in projects with nothing to show.
        var handler = Handler();
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        Pipeline[] pipelines =
        [
            new("Verify_Build", "Verify / Build", "Verify", "Verify", $"{server}/buildConfiguration/Verify_Build"),
            new("Other_Config", "Other / Config", "Other", "Other", $"{server}/buildConfiguration/Other")
        ];
        var builds = await ProviderTestHelpers.Provider("teamcity").FetchBuilds(context, pipelines, 5, Cancel.None);
        await Assert.That(handler.Requests.Count(_ => _.Contains("locator=project:(id:Verify)"))).IsEqualTo(1);
        await Assert.That(handler.Requests.Count(_ => _.Contains("locator=project:(id:Other)"))).IsEqualTo(1);
        await Assert.That(builds.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ABuildWithoutANumberShowsNone()
    {
        // TeamCity numbers a build when it starts, and one that never started is numbered N/A. The
        // build id stood in for a missing number, so a queued row read #9005 and then #119 once the
        // build started, and a build that failed to start read #N/A.
        var builds = await FetchVerifyBuild(
            """
            {"id":9005,"state":"queued","buildTypeId":"Verify_Build","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9005","queuedDate":"20260101T120100+0000"},
            {"id":9003,"number":"N/A","status":"FAILURE","state":"finished","statusText":"Failed to start","buildTypeId":"Verify_Build","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9003","queuedDate":"20260101T115800+0000","finishDate":"20260101T115810+0000"},
            {"id":9002,"number":"118","status":"FAILURE","state":"finished","buildTypeId":"Verify_Build","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9002","queuedDate":"20260101T113000+0000","startDate":"20260101T113100+0000","finishDate":"20260101T113600+0000"}
            """);
        await Verify(builds.Select(Row))
            .Snapshot(
                """
                [
                  Queued, cancel,
                  Failed, retry,
                  #118 Failed, retry
                ]
                """);
    }

    [Test]
    public async Task ABuildRemovedFromTheQueueIsNotListed()
    {
        // TeamCity keeps a build taken off the queue as a canceled build that was never numbered,
        // with the moment it was removed as its queued, start and finish dates, as TeamCity 2026.2
        // answers. Listed, it took the place of the configuration's last real run on the row.
        var builds = await FetchVerifyBuild(
            """
            {"id":9004,"number":"N/A","status":"UNKNOWN","state":"finished","statusText":"Canceled","buildTypeId":"Verify_Build","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9004","queuedDate":"20260101T120000+0000","startDate":"20260101T120000+0000","finishDate":"20260101T120000+0000","canceledInfo":{"text":"Cancelled from BuildMonitor"}},
            {"id":9003,"number":"119","status":"UNKNOWN","state":"finished","statusText":"Canceled","buildTypeId":"Verify_Build","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9003","queuedDate":"20260101T115000+0000","startDate":"20260101T115000+0000","finishDate":"20260101T115500+0000","canceledInfo":{"text":"Stopped"}},
            {"id":9002,"number":"118","status":"FAILURE","state":"finished","buildTypeId":"Verify_Build","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9002","queuedDate":"20260101T113000+0000","startDate":"20260101T113100+0000","finishDate":"20260101T113600+0000"}
            """);
        await Verify(builds.Select(Row))
            .Snapshot(
                """
                [
                  #119 Cancelled, retry,
                  #118 Failed, retry
                ]
                """);
    }

    static Task<IReadOnlyList<Build>> FetchVerifyBuild(string builds)
    {
        var handler = new FakeHttpHandler()
            .Get($"{server}/app/rest/buildTypes", $$$"""{"buildType":[{"id":"Verify_Build","builds":{"build":[{{{builds}}}]}}]}""");
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        Pipeline[] pipelines = [new("Verify_Build", "Verify / Build", "Verify", "Verify", $"{server}/buildConfiguration/Verify_Build")];
        return ProviderTestHelpers.Provider("teamcity").FetchBuilds(context, pipelines, 5, Cancel.None);
    }

    static string Row(Build build)
    {
        List<string> offers = [];
        if (build.CanRetry)
        {
            offers.Add("retry");
        }

        if (build.CanCancel)
        {
            offers.Add("cancel");
        }

        var label = $"{build.RunNumberLabel()} {build.Status}".Trim();
        return $"{label}, {string.Join(" and ", offers)}";
    }

    static PollGroup VerifyProject() =>
        new("Verify", [new("Verify_Build", "Verify / Build", "Verify", "Verify", $"{server}/buildConfiguration/Verify_Build"), new("Verify_Docs", "Verify / Docs", "Verify", "Verify", $"{server}/buildConfiguration/Verify_Docs")]);

    [Test]
    public async Task TheFirstProbeAsksForTheNewestBuild()
    {
        var handler = new FakeHttpHandler()
            .Get($"{server}/app/rest/builds", """{"build":[{"id":9001,"buildTypeId":"Verify_Build"}]}""");
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        var activity = await ProviderTestHelpers.Provider("teamcity").RecentActivity(context, [VerifyProject()], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!["Verify"]).IsEqualTo("9001");
        await Assert.That(handler.Requests.Single()).Contains(",count:1&fields=build(id,buildTypeId)");
    }

    [Test]
    public async Task ALaterProbeAsksOnlyForBuildsSinceTheNewestSeen()
    {
        // TeamCity sends no ETags, so without it a quiet project was fetched whole each time its
        // schedule came round.
        var handler = new FakeHttpHandler()
            .Get($"{server}/app/rest/builds", """{"build":[{"id":9001,"buildTypeId":"Verify_Build"}]}""");
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        var provider = ProviderTestHelpers.Provider("teamcity");
        var first = await provider.RecentActivity(context, [VerifyProject()], ImmutableDictionary<string, string>.Empty, Cancel.None);
        handler.Get($"{server}/app/rest/builds", """{"build":[{"id":9003,"buildTypeId":"Verify_Docs"},{"id":9002,"buildTypeId":"Unwatched"}]}""");
        var second = await provider.RecentActivity(context, [VerifyProject()], first!, Cancel.None);
        await Assert.That(handler.Requests[^1]).Contains("sinceBuild:(id:9001)");
        await Assert.That(second!["Verify"]).IsEqualTo("9003");
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("POST", $"{server}/app/rest/buildQueue", "{}")
            .Map("POST", $"{server}/app/rest/builds/id:9001", "{}");
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("teamcity", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("teamcity");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "120"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://teamcity.example.com/app/rest/buildQueue
                  {"buildType":{"id":"Verify_Build"},"branchName":"main"},
                  POST https://teamcity.example.com/app/rest/builds/id:9001
                  {"comment":"Cancelled from BuildMonitor","readdIntoQueue":false}
                ]
                """);
    }

    [Test]
    public async Task FetchLogDownloadsTheBuildLog()
    {
        var handler = Handler()
            .Get($"{server}/downloadBuildLog.html?buildId=9000", "[Step 2/2] Tests failed: 1\n");
        var context = ProviderTestHelpers.Context("teamcity", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("teamcity", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("teamcity").FetchLog(context, builds.Single(_ => _.RunNumber == "119"), Cancel.None);
        await Assert.That(log).IsEqualTo("[Step 2/2] Tests failed: 1\n");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {server}/downloadBuildLog.html?buildId=9000");
    }

    [Test]
    [Arguments("20260101T120000+0000", "2026-01-01T12:00:00+00:00")]
    [Arguments("20260101T120000+0300", "2026-01-01T12:00:00+03:00")]
    [Arguments("20260101T120000-0500", "2026-01-01T12:00:00-05:00")]
    public async Task ParsesDates(string text, string expected) =>
        await Assert.That(TeamCityDate.Parse(text)).IsEqualTo(DateTimeOffset.Parse(expected));

    [Test]
    public async Task UnreadableDateIsNull() =>
        await Assert.That(TeamCityDate.Parse("soon")).IsNull();
}
