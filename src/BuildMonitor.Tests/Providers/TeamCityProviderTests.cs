public class TeamCityProviderTests
{
    const string server = "https://teamcity.example.com";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{server}/app/rest/buildTypes",
                """
                {"buildType":[
                  {"id":"Verify_Build","name":"Build","projectName":"Verify","projectId":"Verify","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build"},
                  {"id":"Verify_Docs","name":"Docs","projectName":"Verify","projectId":"Verify","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Docs"}
                ]}
                """)
            .Get(
                $"{server}/app/rest/builds",
                """
                {"build":[
                  {"id":9001,"number":"120","status":"SUCCESS","state":"running","branchName":"pull/15","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9001","statusText":"Step 2 of 4","queuedDate":"20260101T115000+0000","startDate":"20260101T115100+0000","buildTypeId":"Verify_Build","running-info":{"percentageComplete":40,"elapsedSeconds":120,"estimatedTotalSeconds":300,"leftSeconds":180},"triggered":{"user":{"username":"simon","name":"Simon"}},"revisions":{"revision":[{"version":"abc123"}]}},
                  {"id":9000,"number":"119","status":"FAILURE","state":"finished","branchName":"main","defaultBranch":true,"webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Build/9000","statusText":"Tests failed: 1","queuedDate":"20260101T100000+0000","startDate":"20260101T100100+0000","finishDate":"20260101T100600+0000","buildTypeId":"Verify_Build"},
                  {"id":8999,"number":"50","status":"UNKNOWN","state":"finished","branchName":"main","webUrl":"https://teamcity.example.com/buildConfiguration/Verify_Docs/8999","statusText":"Canceled","queuedDate":"20260101T090000+0000","startDate":"20260101T090100+0000","finishDate":"20260101T090200+0000","buildTypeId":"Verify_Docs","canceledInfo":{"text":"stopped"}},
                  {"id":8998,"number":"3","status":"SUCCESS","state":"queued","webUrl":"https://teamcity.example.com/buildConfiguration/Other/8998","queuedDate":"20260101T115900+0000","buildTypeId":"Other_Config"}
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
        await Verify(handler.Requests);
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
