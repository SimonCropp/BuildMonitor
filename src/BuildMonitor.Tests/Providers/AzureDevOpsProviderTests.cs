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
                $"{organization}/Web/_apis/build/builds?definitions=1,2&$top=10&queryOrder=queueTimeDescending&api-version=7.1",
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
        await Verify(handler.Requests);
    }

    [Test]
    public async Task ProjectScopeSkipsProjectDiscovery()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/pipelines?api-version=7.1", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: [("organization", "contoso"), ("project", "Web")]);
        await ProviderTestHelpers.Provider("azure-devops").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests);
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
