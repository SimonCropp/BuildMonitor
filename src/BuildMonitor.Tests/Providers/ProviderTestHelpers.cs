static class ProviderTestHelpers
{
    public static ProviderContext Context(string providerId, FakeHttpHandler handler, string? server = null, string? user = null, params (string Id, string Value)[] scope)
    {
        var connection = new Connection
        {
            Id = providerId,
            ProviderId = providerId,
            Name = providerId,
            Server = server,
            User = user,
            Scope = scope.ToImmutableDictionary(_ => _.Id, _ => _.Value)
        };
        return Providers.Context(connection, "secret", handler);
    }

    public static IProvider Provider(string id) =>
        Providers.Get(id);

    public static async Task<IReadOnlyList<Build>> DiscoverAndFetch(string providerId, ProviderContext context, int perPipeline = 5)
    {
        var provider = Provider(providerId);
        var pipelines = await provider.DiscoverPipelines(context, Cancel.None);
        return await provider.FetchBuilds(context, pipelines, perPipeline, Cancel.None);
    }
}
