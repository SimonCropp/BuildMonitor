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
    public async Task HistoryLimitIsSentAsQueuedDate()
    {
        var handler = Handler();
        var context = ProviderTestHelpers.Context("teamcity", handler, server) with { Since = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero) };
        await ProviderTestHelpers.DiscoverAndFetch("teamcity", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("count:5,queuedDate:(date:20260816T000000"))).IsTrue();
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
