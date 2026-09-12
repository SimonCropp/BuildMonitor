public class OctopusProviderTests
{
    const string server = "https://octopus.example.com";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get($"{server}/api/spaces?take=100", """{"Items":[{"Id":"Spaces-1","Name":"Default","IsDefault":true},{"Id":"Spaces-2","Name":"Other","IsDefault":false}]}""")
            .Get($"{server}/api/Spaces-1/projects?take=100", """{"Items":[{"Id":"Projects-1","Name":"Web","Links":{"Web":"/app#/Spaces-1/projects/web"}}]}""")
            .Get($"{server}/api/Spaces-1/environments/all", """[{"Id":"Environments-1","Name":"Production"},{"Id":"Environments-2","Name":"Staging"}]""")
            .Get(
                $"{server}/api/Spaces-1/deployments?take=5",
                """
                {"Items":[
                  {"Id":"Deployments-10","ProjectId":"Projects-1","EnvironmentId":"Environments-1","ReleaseId":"Releases-5","TaskId":"ServerTasks-100","Name":"Deploy to Production","Links":{"Web":"/app#/Spaces-1/deployments/Deployments-10"}},
                  {"Id":"Deployments-9","ProjectId":"Projects-1","EnvironmentId":"Environments-2","ReleaseId":"Releases-5","TaskId":"ServerTasks-99","Name":"Deploy to Staging","Links":{"Web":"/app#/Spaces-1/deployments/Deployments-9"}},
                  {"Id":"Deployments-8","ProjectId":"Projects-2","EnvironmentId":"Environments-2","ReleaseId":"Releases-1","TaskId":"ServerTasks-50"}
                ]}
                """)
            .Get(
                $"{server}/api/Spaces-1/tasks?take=5&name=Deploy",
                """
                {"Items":[
                  {"Id":"ServerTasks-100","State":"Executing","Description":"Deploy Web release 1.2.3 to Production","QueueTime":"2026-01-01T11:57:00Z","StartTime":"2026-01-01T11:58:00Z","Links":{"Details":"/api/Spaces-1/tasks/ServerTasks-100/details","Cancel":"/api/Spaces-1/tasks/ServerTasks-100/cancel","Rerun":"/api/Spaces-1/tasks/rerun/ServerTasks-100","Web":"/app#/Spaces-1/tasks/ServerTasks-100"}}
                ]}
                """)
            .Get($"{server}/api/tasks/ServerTasks-99", """{"Id":"ServerTasks-99","State":"Failed","Description":"Deploy Web release 1.2.3 to Staging","QueueTime":"2026-01-01T10:00:00Z","StartTime":"2026-01-01T10:00:10Z","CompletedTime":"2026-01-01T10:03:00Z","Links":{"Details":"/api/Spaces-1/tasks/ServerTasks-99/details","Cancel":"/api/Spaces-1/tasks/ServerTasks-99/cancel","Rerun":"/api/Spaces-1/tasks/rerun/ServerTasks-99","Web":"/app#/Spaces-1/tasks/ServerTasks-99"}}""")
            .Get($"{server}/api/Spaces-1/tasks/ServerTasks-100/details", """{"Task":{"Id":"ServerTasks-100"},"Progress":{"ProgressPercentage":40,"EstimatedTimeRemaining":"00:02:15"}}""");

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", ProviderTestHelpers.Context("octopus", handler, server));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task RetryAndCancelUseTheTaskLinks()
    {
        var handler = Handler()
            .Map("POST", $"{server}/api/Spaces-1/tasks/rerun/ServerTasks-99", "{}")
            .Map("POST", $"{server}/api/Spaces-1/tasks/ServerTasks-100/cancel", "{}");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("octopus");
        await provider.Retry(context, builds.Single(_ => _.Branch == "Staging"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.Branch == "Production"), Cancel.None);
        await Verify(handler.Requests);
    }

    [Test]
    public async Task NamedSpaceIsUsed()
    {
        var handler = new FakeHttpHandler()
            .Get($"{server}/api/spaces?take=100", """{"Items":[{"Id":"Spaces-1","Name":"Default","IsDefault":true},{"Id":"Spaces-2","Name":"Other","IsDefault":false}]}""")
            .Get($"{server}/api/Spaces-2/projects?take=100", """{"Items":[]}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server, scope: ("space", "other"));
        await ProviderTestHelpers.Provider("octopus").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests);
    }

    [Test]
    public async Task ApiKeyHeader()
    {
        var handler = new FakeHttpHandler().Get($"{server}/api/users/me", """{"Username":"simon","DisplayName":"Simon"}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        await ProviderTestHelpers.Provider("octopus").Test(context, Cancel.None);
        await Assert.That(handler.RequestHeaders.Single().GetValues("X-Octopus-ApiKey").Single()).IsEqualTo("secret");
    }
}
