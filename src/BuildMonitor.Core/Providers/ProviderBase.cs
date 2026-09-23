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

    /// <summary>
    /// Virtual rather than abstract, as <see cref="ListArtifacts"/> is: eight of the ten services
    /// run their queue in the order it was filled and have no call for this. Their descriptors say
    /// so with <see cref="ProviderDescriptor.HasQueuePriority"/>, and nothing asks them.
    /// </summary>
    public virtual Task RunNext(ProviderContext context, Build build, Cancel cancel) =>
        throw new NotSupportedException($"{Descriptor.Name} cannot reorder its build queue");

    public abstract Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel);

    /// <summary>
    /// Virtual rather than abstract, unlike <see cref="FetchLog"/>, because two of the services do
    /// not have artifacts to list: Bitbucket does not expose a run's artifacts over its API at all,
    /// and Travis stores none. Their descriptors say so with
    /// <see cref="ProviderDescriptor.HasArtifacts"/>, and nothing asks them.
    /// </summary>
    public virtual Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel) =>
        Task.FromResult<IReadOnlyList<BuildArtifact>>([]);

    public virtual Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel) =>
        throw new NotSupportedException($"{Descriptor.Name} has no artifacts to download");

    public abstract Task<ConnectionTest> Test(ProviderContext context, Cancel cancel);

    public virtual Task<BuildAccess> Access(ProviderContext context, Cancel cancel) =>
        Task.FromResult(BuildAccess.Unknown);

    /// <summary>
    /// <see cref="Access"/> for a connection test, whose credential has just been proved to work:
    /// a check that fails leaves the answer unknown rather than failing the test.
    /// </summary>
    protected async Task<BuildAccess> AccessOrUnknown(ProviderContext context, Cancel cancel)
    {
        try
        {
            return await Access(context, cancel);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warning(exception, "Asking what {Connection} may do failed", context.Connection.Name);
            return BuildAccess.Unknown;
        }
    }

    public virtual Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel) =>
        Task.FromResult<ImmutableDictionary<string, string>?>(null);

    protected static string Encode(string value) =>
        Uri.EscapeDataString(value);

    /// <summary>
    /// A GET whose 404 means there is nothing there rather than that something broke, such as the
    /// newest run on a branch nothing has built. Thrown, it would fail the whole fetch it was part
    /// of and back the pipeline off.
    /// </summary>
    protected static async Task<T?> GetOrNone<T>(ProviderContext context, string path, JsonTypeInfo<T> info, Cancel cancel)
        where T : class
    {
        try
        {
            return await context.Http.Get(path, info, cancel);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// A path escaped a segment at a time, so a space or a hash in an artifact's name survives
    /// while the separators that make it a path stay separators. Escaping the whole string would
    /// turn a nested artifact's path into one segment with slashes in its name, which no server
    /// has a file at.
    /// </summary>
    protected static string EncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Encode));

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
