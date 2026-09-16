/// <summary>
/// The registry. Order matches <see cref="ProviderDescriptors.All"/>.
/// </summary>
static class Providers
{
    public static readonly IReadOnlyList<IProvider> All =
    [
        new AppVeyorProvider(),
        new AzureDevOpsProvider(),
        new BitbucketProvider(),
        new GitHubProvider(),
        new GitLabProvider(),
        new GoCdProvider(),
        new JenkinsProvider(),
        new OctopusProvider(),
        new TeamCityProvider(),
        new TravisProvider()
    ];

    public static IProvider Get(string id) =>
        All.SingleOrDefault(_ => _.Descriptor.Id == id) ??
        throw new ArgumentException($"Unknown provider: {id}");

    /// <summary>
    /// The client for one connection: base address and headers from the provider, credential
    /// from the secret store, put on the wire as the connection's way of signing in wants,
    /// transport from whoever is calling, which in tests is a fake. A poller passes the
    /// <see cref="ETagCache"/> and <see cref="RateBudget"/> it keeps between polls; a one-off call
    /// such as a retry or a connection test goes without.
    /// </summary>
    public static ProviderContext Context(Connection connection, string? secret, HttpMessageHandler handler, ETagCache? cache = null, RateBudget? budget = null)
    {
        var provider = Get(connection.ProviderId);
        var http = new HttpJson(
            handler,
            provider.BaseAddress(connection),
            provider.Descriptor.SchemeFor(connection.Auth),
            secret,
            connection.User,
            provider.Headers,
            cache,
            budget);
        return new(connection, http);
    }
}
