public class OctopusProviderTests
{
    const string server = "https://octopus.example.com";

    static FakeHttpHandler Discovery() =>
        new FakeHttpHandler()
            .Get($"{server}/api/spaces?take=100", """{"Items":[{"Id":"Spaces-1","Name":"Default","IsDefault":true},{"Id":"Spaces-2","Name":"Other","IsDefault":false}]}""")
            .Get($"{server}/api/Spaces-1/projects?take=100", """{"Items":[{"Id":"Projects-1","Name":"Web","Links":{"Web":"/app#/Spaces-1/projects/web"}}]}""")
            .Get($"{server}/api/Spaces-1/tasks/ServerTasks-100/details?verbose=false&tail=1", """{"Task":{"Id":"ServerTasks-100"},"Progress":{"ProgressPercentage":40,"EstimatedTimeRemaining":"00:02:15"}}""");

    static FakeHttpHandler Handler() =>
        Discovery()
            .Get(
                $"{server}/api/Spaces-1/dashboard/dynamic",
                """
                {"Items":[
                  {"ProjectId":"Projects-1","EnvironmentId":"Environments-1","DeploymentId":"Deployments-10","TaskId":"ServerTasks-100","ReleaseVersion":"1.2.3","State":"Executing","QueueTime":"2026-01-01T11:57:00Z","StartTime":"2026-01-01T11:58:00Z","IsCurrent":true},
                  {"ProjectId":"Projects-1","EnvironmentId":"Environments-2","DeploymentId":"Deployments-9","TaskId":"ServerTasks-99","ReleaseVersion":"1.2.3","State":"Failed","QueueTime":"2026-01-01T10:00:00Z","StartTime":"2026-01-01T10:00:10Z","CompletedTime":"2026-01-01T10:03:00Z","IsCurrent":true},
                  {"ProjectId":"Projects-2","EnvironmentId":"Environments-2","DeploymentId":"Deployments-8","TaskId":"ServerTasks-50","ReleaseVersion":"0.1","State":"Success"}
                ],"Environments":[{"Id":"Environments-1","Name":"Production"},{"Id":"Environments-2","Name":"Staging"}]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", ProviderTestHelpers.Context("octopus", handler, server));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task AnExecutingDeploymentReadsItsProgress()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", ProviderTestHelpers.Context("octopus", handler, server));
        await Assert.That(handler.Requests).Contains($"GET {server}/api/Spaces-1/tasks/ServerTasks-100/details?verbose=false&tail=1");
        await Assert.That(builds.Single(_ => _.Branch == "Production").Estimate!.Remaining).IsEqualTo(TimeSpan.FromSeconds(135));
    }

    const string requestedFor = "d1a80549-4d1f-642e-b5d5-9eca49ca5e24";

    /// <summary>
    /// A deployment of a release a pipeline created, whose notes carry the user id of whoever it
    /// was created for and nothing that could name them.
    /// </summary>
    static FakeHttpHandler Released(string notes) =>
        Discovery()
            .Get(
                $"{server}/api/Spaces-1/dashboard/dynamic",
                """
                {"Items":[
                  {"ProjectId":"Projects-1","EnvironmentId":"Environments-1","DeploymentId":"Deployments-10","TaskId":"ServerTasks-99","ReleaseId":"Releases-5","ReleaseVersion":"1.2.3","State":"Failed","QueueTime":"2026-01-01T10:00:00Z","StartTime":"2026-01-01T10:00:10Z","CompletedTime":"2026-01-01T10:03:00Z","IsCurrent":true}
                ],"Environments":[{"Id":"Environments-1","Name":"Production"}]}
                """)
            .Get($"{server}/api/Spaces-1/releases/Releases-5", $$"""{"Id":"Releases-5","ReleaseNotes":{{JsonSerializer.Serialize(notes)}}}""");

    static ProviderContext Context(FakeHttpHandler handler, IdentityNames? identities = null) =>
        ProviderTestHelpers.Context("octopus", handler, server) with
        {
            Identities = identities ?? new()
        };

    [Test]
    public async Task ANamedIdInTheReleaseNotesNamesTheDeployment()
    {
        var identities = new IdentityNames();
        identities.Add(requestedFor, "Simon Cropp");
        var handler = Released($$"""{ "AzureDevOpsRequestedForId": "{{requestedFor}}" }""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", Context(handler, identities));
        await Assert.That(builds.Single().Author).IsEqualTo("Simon Cropp");
    }

    /// <summary>
    /// Nothing in Octopus can name the id, so until another connection does the row names no one,
    /// as every Octopus row did before.
    /// </summary>
    [Test]
    [Arguments($$"""{ "AzureDevOpsRequestedForId": "{{requestedFor}}" }""")]
    [Arguments("""{ "AzureDevOpsRequestedForId": "" }""")]
    [Arguments("""{ "Something": "else" }""")]
    [Arguments("Deployed the thing")]
    [Arguments("{not json")]
    [Arguments("")]
    public async Task AnIdNothingHasNamedLeavesTheDeploymentUnnamed(string notes)
    {
        var handler = Released(notes);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", Context(handler));
        await Assert.That(builds.Single().Author).IsNull();
    }

    [Test]
    public async Task AReleaseIsReadOnce()
    {
        var handler = Released($$"""{ "AzureDevOpsRequestedForId": "{{requestedFor}}" }""");
        var context = Context(handler);
        await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("/releases/"))).IsFalse();
    }

    static FakeHttpHandler Limited() =>
        Discovery()
            .Get($"{server}/api/Spaces-1/dashboard/dynamic", """{"Items":[],"Environments":[],"ProjectLimit":0}""")
            .Get($"{server}/api/Spaces-1/environments/all", """[{"Id":"Environments-1","Name":"Production"},{"Id":"Environments-2","Name":"Staging"}]""")
            .Get($"{server}/api/Spaces-1/deployments?take=5", """{"Items":[{"Id":"Deployments-10","ProjectId":"Projects-1","EnvironmentId":"Environments-1","TaskId":"ServerTasks-100","Links":{"Web":"/app#/Spaces-1/deployments/Deployments-10"}}]}""")
            .Get(
                $"{server}/api/Spaces-1/tasks?take=5&name=Deploy",
                """{"Items":[{"Id":"ServerTasks-100","State":"Executing","Description":"Deploy Web release 1.2.3 to Production","QueueTime":"2026-01-01T11:57:00Z","StartTime":"2026-01-01T11:58:00Z","Links":{"Details":"/api/Spaces-1/tasks/ServerTasks-100/details{?verbose,tail,ranges}","Cancel":"/api/Spaces-1/tasks/ServerTasks-100/cancel","Rerun":"/api/Spaces-1/tasks/rerun/ServerTasks-100"}}]}""");

    [Test]
    public async Task ALimitedDashboardFallsBackToSeparateListings()
    {
        var handler = Limited();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", ProviderTestHelpers.Context("octopus", handler, server));
        await Assert.That(builds.Single().RunNumber).IsEqualTo("1.2.3");
        // The task's templated details link is requested without its template.
        await Assert.That(handler.Requests).Contains($"GET {server}/api/Spaces-1/tasks/ServerTasks-100/details?verbose=false&tail=1");
    }

    [Test]
    public async Task ALimitedDashboardIsRememberedUntilTheProjectsAreListedAgain()
    {
        // Asked every poll, the dashboard was downloaded only to be thrown away, and the environments
        // were read again beside it.
        var handler = Limited();
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        var provider = ProviderTestHelpers.Provider("octopus");
        var pipelines = await provider.DiscoverPipelines(context, Cancel.None);
        await provider.FetchBuilds(context, pipelines, 5, Cancel.None);
        await provider.FetchBuilds(context, pipelines, 5, Cancel.None);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/dashboard/dynamic"))).IsEqualTo(1);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/environments/all"))).IsEqualTo(1);

        await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/dashboard/dynamic"))).IsEqualTo(2);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/environments/all"))).IsEqualTo(2);
    }

    [Test]
    public async Task TasksMissingFromThePageAreFetchedTogetherFromTheirSpace()
    {
        // Each was a request of its own, and its route left out the space, which reads the default
        // space only.
        var handler = Limited()
            .Get(
                $"{server}/api/Spaces-1/deployments?take=5",
                """{"Items":[{"Id":"Deployments-10","ProjectId":"Projects-1","EnvironmentId":"Environments-1","TaskId":"ServerTasks-100"},{"Id":"Deployments-9","ProjectId":"Projects-1","EnvironmentId":"Environments-2","TaskId":"ServerTasks-99"}]}""")
            .Get($"{server}/api/Spaces-1/tasks?take=5&name=Deploy", """{"Items":[]}""")
            .Get(
                $"{server}/api/Spaces-1/tasks?ids=ServerTasks-100,ServerTasks-99&take=2",
                """{"Items":[{"Id":"ServerTasks-100","State":"Success","Description":"Deploy Web release 1.2.3 to Production"},{"Id":"ServerTasks-99","State":"Failed","Description":"Deploy Web release 1.2.2 to Staging"}]}""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", ProviderTestHelpers.Context("octopus", handler, server));
        await Assert.That(builds.Select(_ => $"{_.Branch} {_.RunNumber}")).IsEquivalentTo(["Production 1.2.3", "Staging 1.2.2"]);
        await Assert.That(handler.Requests.Count(_ => _.Contains("/tasks?ids="))).IsEqualTo(1);
    }

    [Test]
    public async Task CancelTheTaskAndNeverRetry()
    {
        var handler = Handler()
            .Map("POST", $"{server}/api/Spaces-1/tasks/ServerTasks-100/cancel", "{}");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        handler.Requests.Clear();
        await Assert.That(builds.Any(_ => _.CanRetry)).IsFalse();
        await ProviderTestHelpers.Provider("octopus").Cancel(context, builds.Single(_ => _.Branch == "Production"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://octopus.example.com/api/Spaces-1/tasks/ServerTasks-100/cancel
                ]
                """);
    }

    [Test]
    public async Task FetchLogReadsTheTaskLogFromItsSpace()
    {
        var handler = Handler()
            .Get($"{server}/api/Spaces-1/tasks/ServerTasks-99/raw", "Step 2 failed: exit code 1\n");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("octopus").FetchLog(context, builds.Single(_ => _.Branch == "Staging"), Cancel.None);
        await Assert.That(log).IsEqualTo("Step 2 failed: exit code 1\n");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {server}/api/Spaces-1/tasks/ServerTasks-99/raw");
    }

    [Test]
    public async Task NamedSpaceIsUsed()
    {
        var handler = new FakeHttpHandler()
            .Get($"{server}/api/spaces?take=100", """{"Items":[{"Id":"Spaces-1","Name":"Default","IsDefault":true},{"Id":"Spaces-2","Name":"Other","IsDefault":false}]}""")
            .Get($"{server}/api/Spaces-2/projects?take=100", """{"Items":[]}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server, scope: ("space", "other"));
        await ProviderTestHelpers.Provider("octopus").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://octopus.example.com/api/spaces?take=100,
                  GET https://octopus.example.com/api/Spaces-2/projects?take=100
                ]
                """);
    }

    [Test]
    public async Task ApiKeyHeader()
    {
        var handler = new FakeHttpHandler().Get($"{server}/api/users/me", """{"Username":"simon","DisplayName":"Simon"}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        await ProviderTestHelpers.Provider("octopus").Test(context, Cancel.None);
        // The test's own requests and those asking what the key may do alike.
        await Assert.That(handler.RequestHeaders.Select(_ => _.GetValues("X-Octopus-ApiKey").Single()).Distinct()).IsEquivalentTo(["secret"]);
    }

    const string permissions = $"{server}/api/users/Users-1/permissions?spaces=Spaces-1&includeSystem=true";

    static FakeHttpHandler Permissions(FakeHttpHandler handler, string answer) =>
        handler
            .Get($"{server}/api/users/me", """{"Id":"Users-1","Username":"simon","DisplayName":"Simon"}""")
            .Get(permissions, answer);

    [Test]
    [Arguments("""{"SpacePermissions":{"ProjectView":[{"SpaceId":"Spaces-1"}]},"SystemPermissions":["SpaceView"],"IsPermissionsComplete":true}""", nameof(BuildAccess.Watch))]
    [Arguments("""{"SpacePermissions":{"TaskCancel":[{"SpaceId":"Spaces-2"}]},"IsPermissionsComplete":true}""", nameof(BuildAccess.Watch))]
    [Arguments("""{"SpacePermissions":{"TaskCancel":[{"SpaceId":"Spaces-1","RestrictedToProjectIds":[],"RestrictedToEnvironmentIds":[],"RestrictedToTenantIds":[],"RestrictedToProjectGroupIds":[]}]},"IsPermissionsComplete":true}""", nameof(BuildAccess.Change))]
    [Arguments("""{"SpacePermissions":{"TaskCancel":[{"SpaceId":"Spaces-1","RestrictedToEnvironmentIds":["Environments-2"]}]},"IsPermissionsComplete":true}""", nameof(BuildAccess.Unknown))]
    [Arguments("""{"SpacePermissions":{"TaskCancel":[{"SpaceId":"Spaces-1","RestrictedToTenantIds":["Tenants-1"]}]},"IsPermissionsComplete":true}""", nameof(BuildAccess.Unknown))]
    [Arguments("""{"SpacePermissions":{},"IsPermissionsComplete":false}""", nameof(BuildAccess.Unknown))]
    [Arguments("""{"SpacePermissions":{},"SystemPermissions":["AdministerSystem"],"IsPermissionsComplete":true}""", nameof(BuildAccess.Unknown))]
    public async Task TaskCancelInTheSpaceDecides(string answer, string expected)
    {
        var context = ProviderTestHelpers.Context("octopus", Permissions(Discovery(), answer), server);
        var access = await ProviderTestHelpers.Provider("octopus").Access(context, Cancel.None);
        await Assert.That(access.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Environments-1", true)]
    [Arguments("Environments-2", false)]
    public async Task AGrantLimitedToEnvironmentsDecidesPerDeployment(string environment, bool cancellable)
    {
        var handler = Permissions(Handler(), $$"""{"SpacePermissions":{"TaskCancel":[{"SpaceId":"Spaces-1","RestrictedToEnvironmentIds":["{{environment}}"]}]},"IsPermissionsComplete":true}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        await ProviderTestHelpers.Provider("octopus").Access(context, Cancel.None);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        // The deployment to Production, Environments-1, is the one executing.
        await Assert.That(builds.Single(_ => _.Branch == "Production").CanCancel).IsEqualTo(cancellable);
    }

    [Test]
    public async Task AGrantLimitedToOtherProjectsTakesCancelOffTheSeparateListings()
    {
        var handler = Permissions(Limited(), """{"SpacePermissions":{"TaskCancel":[{"SpaceId":"Spaces-1","RestrictedToProjectIds":["Projects-9"]}]},"IsPermissionsComplete":true}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        await ProviderTestHelpers.Provider("octopus").Access(context, Cancel.None);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("octopus", context);
        await Assert.That(builds.Single().CanCancel).IsFalse();
    }

    [Test]
    public async Task TheTestSaysAKeyCanOnlyWatch()
    {
        var handler = Permissions(Discovery(), """{"SpacePermissions":{},"IsPermissionsComplete":true}""");
        var context = ProviderTestHelpers.Context("octopus", handler, server);
        var result = await ProviderTestHelpers.Provider("octopus").Test(context, Cancel.None);
        await Assert.That(result.Describe(ProviderDescriptors.Octopus))
            .IsEqualTo("Signed in as Simon. The connection can watch builds but not change them. Octopus Deploy needs the TaskCancel permission in the space");
    }
}
