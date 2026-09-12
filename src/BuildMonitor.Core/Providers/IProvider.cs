/// <summary>
/// One CI service. Everything a provider knows about its API lives behind these five calls;
/// nothing else in the app composes a URL for it.
/// </summary>
interface IProvider
{
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// The base every relative path is resolved against, for this connection.
    /// </summary>
    Uri BaseAddress(Connection connection);

    /// <summary>
    /// Headers every request carries beyond the credential: API versions, accept types.
    /// </summary>
    IEnumerable<KeyValuePair<string, string>> Headers { get; }

    Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel);

    /// <summary>
    /// Recent builds of the given pipelines, newest first per pipeline, at most
    /// <paramref name="perPipeline"/> each.
    /// </summary>
    Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel);

    Task Retry(ProviderContext context, Build build, Cancel cancel);

    Task Cancel(ProviderContext context, Build build, Cancel cancel);

    /// <summary>
    /// Proves the credential works, and says who it belongs to when the API tells.
    /// </summary>
    Task<ConnectionTest> Test(ProviderContext context, Cancel cancel);
}
