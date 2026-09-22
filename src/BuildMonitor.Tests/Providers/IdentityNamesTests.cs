public class IdentityNamesTests
{
    const string id = "d1a80549-4d1f-642e-b5d5-9eca49ca5e24";

    [Test]
    public async Task AnIdIsTheOneEntryWhateverItsCase()
    {
        var names = new IdentityNames();
        names.Add(id.ToUpperInvariant(), "Simon Cropp");
        await Assert.That(names.Name(id)).IsEqualTo("Simon Cropp");
        await Assert.That(names.Name($"{{{id}}}")).IsEqualTo("Simon Cropp");
    }

    [Test]
    [Arguments("Users-123")]
    [Arguments("")]
    [Arguments(null)]
    public async Task AnIdThatIsNoGuidNamesNobody(string? other)
    {
        var names = new IdentityNames();
        names.Add(other, "Simon Cropp");
        await Assert.That(names.Name(other)).IsNull();
    }

    [Test]
    public async Task AnIdNothingHasNamedNamesNobody() =>
        await Assert.That(new IdentityNames().Name(id)).IsNull();

    /// <summary>
    /// What the whole of it is for: Azure DevOps names the identity its build was queued for, and
    /// an Octopus release a pipeline created, carrying that id and no name, is named by it. The
    /// poller shares the one <see cref="IdentityNames"/> between every connection it runs.
    /// </summary>
    [Test]
    public async Task ANameOneConnectionLearnsNamesAnother()
    {
        var shared = new IdentityNames();
        var azure = new FakeHttpHandler()
            .Get(
                "https://dev.azure.com/contoso/_apis/projects?api-version=7.1&$top=100",
                """{"count":1,"value":[{"id":"p1","name":"Web"}]}""")
            .Get(
                "https://dev.azure.com/contoso/Web/_apis/pipelines?api-version=7.1",
                """{"count":1,"value":[{"id":1,"name":"CI"}]}""")
            .Get(
                "https://dev.azure.com/contoso/Web/_apis/build/builds",
                $$$"""
                   {"count":1,"value":[
                     {"id":301,"buildNumber":"1","status":"completed","result":"succeeded","queueTime":"2026-01-01T11:50:00Z","requestedFor":{"id":"{{{id}}}","displayName":"Simon Cropp"},"definition":{"id":1,"name":"CI"}}
                   ]}
                   """);
        var octopus = new FakeHttpHandler()
            .Get("https://octopus.example.com/api/spaces?take=100", """{"Items":[{"Id":"Spaces-1","Name":"Default","IsDefault":true}]}""")
            .Get("https://octopus.example.com/api/Spaces-1/projects?take=100", """{"Items":[{"Id":"Projects-1","Name":"Web"}]}""")
            .Get(
                "https://octopus.example.com/api/Spaces-1/dashboard/dynamic",
                """
                {"Items":[
                  {"ProjectId":"Projects-1","EnvironmentId":"Environments-1","DeploymentId":"Deployments-10","TaskId":"ServerTasks-99","ReleaseId":"Releases-5","ReleaseVersion":"1.2.3","State":"Failed","QueueTime":"2026-01-01T12:00:00Z"}
                ],"Environments":[{"Id":"Environments-1","Name":"Production"}]}
                """)
            .Get(
                "https://octopus.example.com/api/Spaces-1/releases/Releases-5",
                $$"""{"Id":"Releases-5","ReleaseNotes":"{ \"AzureDevOpsRequestedForId\": \"{{id}}\" }"}""");

        var deployments = await ProviderTestHelpers.DiscoverAndFetch(
            "octopus",
            ProviderTestHelpers.Context("octopus", octopus, "https://octopus.example.com")
                with
                {
                    Identities = shared
                });
        // Nothing has named the id yet, so the deployment names no one.
        await Assert.That(deployments.Single().Author).IsNull();

        await ProviderTestHelpers.DiscoverAndFetch(
            "azure-devops",
            ProviderTestHelpers.Context("azure-devops", azure, scope: ("organization", "contoso")) with
            {
                Identities = shared
            });

        var named = await ProviderTestHelpers.DiscoverAndFetch(
            "octopus",
            ProviderTestHelpers.Context("octopus", octopus, "https://octopus.example.com") with
            {
                Identities = shared
            });
        await Assert.That(named.Single().Author).IsEqualTo("Simon Cropp");
    }
}
