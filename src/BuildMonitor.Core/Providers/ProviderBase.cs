/// <summary>
/// What every provider shares: the base address rule and a few conversions.
/// </summary>
abstract class ProviderBase : IProvider
{
    public abstract ProviderDescriptor Descriptor { get; }

    public virtual Uri BaseAddress(Connection connection)
    {
        var server = connection.Server ?? Descriptor.DefaultServer ??
                     throw new InvalidOperationException($"{Descriptor.Name} needs a server");
        return new(server.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public virtual IEnumerable<KeyValuePair<string, string>> Headers => [];

    public abstract Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel);

    public abstract Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel);

    public abstract Task Retry(ProviderContext context, Build build, Cancel cancel);

    public abstract Task Cancel(ProviderContext context, Build build, Cancel cancel);

    public abstract Task<ConnectionTest> Test(ProviderContext context, Cancel cancel);

    protected static string Encode(string value) =>
        Uri.EscapeDataString(value);

    /// <summary>
    /// The pieces a <see cref="Build.ProviderRef"/> was composed from.
    /// </summary>
    protected static string[] Split(Build build) =>
        build.ProviderRef.Split('|');

    protected static string Join(params string?[] parts) =>
        string.Join('|', parts.Select(_ => _ ?? ""));
}
