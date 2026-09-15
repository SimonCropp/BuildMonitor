public class AzureDevOpsProviderTests
{
    const string organization = "https://dev.azure.com/contoso";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{organization}/_apis/projects?api-version=7.1&$top=100",
                """{"count":1,"value":[{"id":"p1","name":"Web"}]}""")
            .Get(
                $"{organization}/Web/_apis/pipelines?api-version=7.1",
                """{"count":2,"value":[{"id":1,"name":"CI","folder":"\\","_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build?definitionId=1"}}},{"id":2,"name":"Nightly","folder":"\\ops"}]}""")
            .Get(
                $"{organization}/Web/_apis/build/builds?definitions=1,2&maxBuildsPerDefinition=5&queryOrder=queueTimeDescending&api-version=7.1",
                """
                {"count":3,"value":[
                  {"id":301,"buildNumber":"20260101.3","status":"inProgress","result":null,"queueTime":"2026-01-01T11:50:00Z","startTime":"2026-01-01T11:51:00Z","sourceBranch":"refs/heads/main","sourceVersion":"abc123","reason":"individualCI","requestedFor":{"displayName":"Simon"},"definition":{"id":1,"name":"CI"},"repository":{"id":"r1","type":"TfsGit","name":"Web"},"triggerInfo":{"ci.message":"Fix"},"_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build/results?buildId=301"}}},
                  {"id":300,"buildNumber":"20260101.2","status":"completed","result":"failed","queueTime":"2026-01-01T10:50:00Z","startTime":"2026-01-01T10:51:00Z","finishTime":"2026-01-01T10:59:00Z","sourceBranch":"refs/pull/55/merge","sourceVersion":"def456","reason":"pullRequest","requestedFor":{"displayName":"Simon"},"definition":{"id":1,"name":"CI"},"repository":{"id":"VerifyTests/DiffEngine","type":"GitHub","name":"VerifyTests/DiffEngine"},"_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build/results?buildId=300"}}},
                  {"id":290,"buildNumber":"20251231.1","status":"completed","result":"succeeded","queueTime":"2025-12-31T01:00:00Z","startTime":"2025-12-31T01:01:00Z","finishTime":"2025-12-31T01:30:00Z","sourceBranch":"refs/heads/main","sourceVersion":"111","definition":{"id":2,"name":"Nightly"},"repository":{"id":"r1","type":"TfsGit","name":"Web"}}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso")));
        await Verify(new { builds, handler.Requests });
    }

    static PollGroup Web() =>
        new("Web", [new("Web/1", "CI", "Web", "Web", "https://dev.azure.com/contoso/Web"), new("Web/2", "Nightly", "Web", "Web", "https://dev.azure.com/contoso/Web")]);

    [Test]
    public async Task TheFirstProbeAsksForTheNewestBuild()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":1,"value":[{"id":301,"queueTime":"2026-01-01T11:50:00Z","definition":{"id":1}}]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var activity = await ProviderTestHelpers.Provider("azure-devops").RecentActivity(context, [Web()], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!["Web"]).IsEqualTo("2026-01-01T11:50:00.0000000+00:00");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {organization}/Web/_apis/build/builds?definitions=1,2&$top=1&queryOrder=queueTimeDescending&api-version=7.1");
    }

    [Test]
    public async Task ALaterProbeAsksOnlyForBuildsQueuedSince()
    {
        const string seen = "2026-01-01T11:50:00.0000000+00:00";
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var activity = await ProviderTestHelpers.Provider("azure-devops").RecentActivity(context, [Web()], ImmutableDictionary<string, string>.Empty.Add("Web", seen), Cancel.None);
        await Assert.That(activity!["Web"]).IsEqualTo(seen);
        await Assert.That(handler.Requests.Single()).Contains("&minTime=2026-01-01T11");
    }

    [Test]
    public async Task ABuildQueuedSinceMovesTheToken()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":1,"value":[{"id":302,"queueTime":"2026-01-01T11:58:00Z","definition":{"id":2}}]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var activity = await ProviderTestHelpers.Provider("azure-devops").RecentActivity(context, [Web()], ImmutableDictionary<string, string>.Empty.Add("Web", "2026-01-01T11:50:00.0000000+00:00"), Cancel.None);
        await Assert.That(activity!["Web"]).IsEqualTo("2026-01-01T11:58:00.0000000+00:00");
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("PATCH", $"{organization}/Web/_apis/build/builds/300?retry=true&api-version=7.1", "{}")
            .Map("PATCH", $"{organization}/Web/_apis/build/builds/301?api-version=7.1", "{}");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("azure-devops");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "20260101.3"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  PATCH https://dev.azure.com/contoso/Web/_apis/build/builds/300?retry=true&api-version=7.1
                  {},
                  PATCH https://dev.azure.com/contoso/Web/_apis/build/builds/301?api-version=7.1
                  {"status":"cancelling"}
                ]
                """);
    }

    [Test]
    public async Task FetchLogOfTheFailedTasksAsText()
    {
        var handler = Handler()
            .Get(
                $"{organization}/Web/_apis/build/builds/300/timeline?api-version=7.1",
                """
                {"records":[
                  {"id":"t2","parentId":"j1","type":"Task","name":"Test","result":"failed","log":{"id":5}},
                  {"id":"j1","parentId":"p1","type":"Job","name":"Build","result":"failed","log":{"id":3}},
                  {"id":"t1","parentId":"j1","type":"Task","name":"Checkout","result":"succeeded","log":{"id":4}},
                  {"id":"p1","type":"Phase","name":"Build","result":"failed"}
                ]}
                """)
            .Get($"{organization}/Web/_apis/build/builds/300/logs/5?api-version=7.1", "##[error]1 test failed\n");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("azure-devops").FetchLog(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> Build / Test <==\n##[error]1 test failed");
        await Assert.That(handler.RequestHeaders[^1].Accept.ToString()).IsEqualTo("text/plain");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            $"GET {organization}/Web/_apis/build/builds/300/timeline?api-version=7.1",
            $"GET {organization}/Web/_apis/build/builds/300/logs/5?api-version=7.1"
        ]);
    }

    [Test]
    public async Task AJobThatFailedWithNoFailedTaskGivesItsOwnLog()
    {
        var handler = Handler()
            .Get(
                $"{organization}/Web/_apis/build/builds/300/timeline?api-version=7.1",
                """{"records":[{"id":"j1","type":"Job","name":"Build","result":"failed","log":{"id":3}}]}""")
            .Get($"{organization}/Web/_apis/build/builds/300/logs/3?api-version=7.1", "The agent was lost\n");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        var log = await ProviderTestHelpers.Provider("azure-devops").FetchLog(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> Build <==\nThe agent was lost");
    }

    [Test]
    public async Task ProjectScopeSkipsProjectDiscovery()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/pipelines?api-version=7.1", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: [("organization", "contoso"), ("project", "Web")]);
        await ProviderTestHelpers.Provider("azure-devops").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://dev.azure.com/contoso/Web/_apis/pipelines?api-version=7.1
                ]
                """);
    }

    [Test]
    public async Task PatUsesBasicWithEmptyUser()
    {
        var handler = new FakeHttpHandler().Get($"{organization}/_apis/projects?api-version=7.1&$top=100", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        await ProviderTestHelpers.Provider("azure-devops").Test(context, Cancel.None);
        var expected = Convert.ToBase64String(":secret"u8.ToArray());
        await Assert.That(handler.RequestHeaders.Single().Authorization!.ToString()).IsEqualTo($"Basic {expected}");
    }
}
