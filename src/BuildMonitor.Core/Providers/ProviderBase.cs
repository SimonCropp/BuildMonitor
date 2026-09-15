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
        return new(ServerAddress.Normalize(server) + "/", UriKind.Absolute);
    }

    public virtual IEnumerable<KeyValuePair<string, string>> Headers => [];

    public abstract Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel);

    public abstract Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel);

    public abstract Task Retry(ProviderContext context, Build build, Cancel cancel);

    public abstract Task Cancel(ProviderContext context, Build build, Cancel cancel);

    public abstract Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel);

    public abstract Task<ConnectionTest> Test(ProviderContext context, Cancel cancel);

    public virtual Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel) =>
        Task.FromResult<ImmutableDictionary<string, string>?>(null);

    protected static string Encode(string value) =>
        Uri.EscapeDataString(value);

    /// <summary>
    /// The pieces a <see cref="Build.ProviderRef"/> was composed from.
    /// </summary>
    protected static string[] Split(Build build) =>
        build.ProviderRef.Split('|');

    protected static string Join(params string?[] parts) =>
        string.Join('|', parts.Select(_ => _ ?? ""));

    /// <summary>
    /// The logs of the jobs that failed as one text, each under a line naming its job, so two logs
    /// pasted together do not read as one.
    /// </summary>
    protected static string Sections(IEnumerable<(string Name, string Log)> logs) =>
        string.Join("\n\n", logs.Select(_ => $"==> {_.Name} <==\n{_.Log.TrimEnd()}"));
}
