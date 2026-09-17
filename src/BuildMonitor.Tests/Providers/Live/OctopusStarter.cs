using System.Text.Json.Nodes;

/// <summary>
/// Deploys the Octopus sandbox project's newest release, which is what a user does instead of a
/// retry. The provider offers no retry: Octopus answers a deployment task rerun with 400.
/// <para>
/// Every request names the sandbox's space, which discovery put in Pipeline.Group. A route
/// without the space reads the default space only.
/// </para>
/// </summary>
static class OctopusStarter
{
    public static async Task Deploy(LiveConnection live, ProviderContext context, Pipeline sandbox, Cancel cancel)
    {
        var space = sandbox.Group ?? throw new InvalidOperationException("octopus: the sandbox project has no space");
        var releases = await context.Http.GetText($"{space}/projects/{Uri.EscapeDataString(sandbox.Id)}/releases?take=1", cancel);
        string? release;
        using (var document = JsonDocument.Parse(releases))
        {
            release = document.RootElement
                .GetProperty("Items")
                .EnumerateArray()
                .Select(_ => _.GetProperty("Id").GetString())
                .FirstOrDefault();
        }

        if (release is null)
        {
            throw new InvalidOperationException("octopus: the sandbox project has no release to deploy. Create one.");
        }

        var environments = await context.Http.Get($"{space}/environments/all", OctopusContext.Default.ListOctopusEnvironment, cancel);
        var wanted = live.DeployEnvironment;
        var environment = environments.FirstOrDefault(_ => wanted is null ||
                                                           _.Id == wanted ||
                                                           string.Equals(_.Name, wanted, StringComparison.OrdinalIgnoreCase)) ??
                          throw new InvalidOperationException($"octopus: none of the space's {environments.Count} environments is {LiveSettings.Prefix(live.Id)}ENVIRONMENT '{wanted}'");
        var body = new JsonObject
        {
            ["ReleaseId"] = release,
            ["EnvironmentId"] = environment.Id
        };
        LiveLog.Line($"octopus: deploying the newest release to {environment.Id}");
        await context.Http.Send(HttpMethod.Post, $"{space}/deployments", HttpJson.Json(body.ToJsonString()), cancel);
    }
}
