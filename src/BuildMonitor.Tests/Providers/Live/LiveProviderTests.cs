/// <summary>
/// Runs a provider against its real service. Never part of a normal run: each test is explicit
/// and reads its credential from the environment, so a developer with a token can check the
/// wire format still matches without any of it reaching the repository.
/// <para>
/// Variables: <c>BUILDMONITOR_{PROVIDER}_TOKEN</c>, optionally <c>_SERVER</c>, <c>_USER</c>, and
/// <c>_SCOPE_{ID}</c> for each scope field the provider declares. PROVIDER is the descriptor id
/// upper cased with hyphens as underscores, so Azure DevOps reads
/// <c>BUILDMONITOR_AZURE_DEVOPS_TOKEN</c>.
/// </para>
/// </summary>
[Explicit]
public class LiveProviderTests
{
    [Test]
    [Arguments("appveyor")]
    [Arguments("travis")]
    [Arguments("jenkins")]
    [Arguments("github")]
    [Arguments("azure-devops")]
    [Arguments("teamcity")]
    [Arguments("gitlab")]
    [Arguments("gocd")]
    [Arguments("bitbucket")]
    [Arguments("octopus")]
    public async Task DiscoverAndFetch(string providerId)
    {
        var prefix = $"BUILDMONITOR_{providerId.ToUpperInvariant().Replace('-', '_')}_";
        var token = Environment.GetEnvironmentVariable($"{prefix}TOKEN");
        if (token is null)
        {
            Skip.Test($"{prefix}TOKEN is not set.");
        }

        var descriptor = ProviderDescriptors.Get(providerId);
        var scope = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var field in descriptor.Scopes)
        {
            var value = Environment.GetEnvironmentVariable($"{prefix}SCOPE_{field.Id.ToUpperInvariant()}");
            if (value is not null)
            {
                scope[field.Id] = value;
            }
        }

        var connection = new Connection
        {
            Id = providerId,
            ProviderId = providerId,
            Name = descriptor.Name,
            Server = Environment.GetEnvironmentVariable($"{prefix}SERVER"),
            User = Environment.GetEnvironmentVariable($"{prefix}USER"),
            Scope = scope.ToImmutable()
        };
        using var handler = new HttpClientHandler();
        var context = Providers.Context(connection, token, handler);
        var provider = Providers.Get(providerId);

        var test = await provider.Test(context, Cancel.None);
        await Assert.That(test.Ok).IsTrue().Because(test.Message);

        var pipelines = await provider.DiscoverPipelines(context, Cancel.None);
        Console.WriteLine($"{pipelines.Count} pipelines");
        var builds = await provider.FetchBuilds(context, pipelines, 3, Cancel.None);
        Console.WriteLine($"{builds.Count} builds");
        foreach (var build in builds.Take(10))
        {
            Console.WriteLine($"  {build.PipelineName} {build.Branch} {build.RunNumberLabel()} {build.Status} {build.BuildUrl}");
        }

        await Assert.That(pipelines.Count).IsGreaterThan(0);
    }
}
