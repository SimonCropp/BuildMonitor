/// <summary>
/// One CI service. Everything a provider knows about its API lives behind these calls; nothing
/// else in the app composes a URL for it.
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
    /// The log of a failed build, as text: the logs of the jobs, steps or tasks that failed, each
    /// under its name, or the whole build's where the service keeps one log a build. Empty when
    /// nothing that failed has a log, such as a run that failed before it started a job.
    /// </summary>
    Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel);

    /// <summary>
    /// The files a build produced, as the service lists them. Nothing is downloaded: the names and
    /// the sizes are what lets a budget be spent on the files most likely to say why the build
    /// failed. Empty for a build that produced none, and for a service with no artifact API, which
    /// <see cref="ProviderDescriptor.HasArtifacts"/> says before anything asks.
    /// </summary>
    Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel);

    /// <summary>
    /// One artifact, copied to <paramref name="destination"/> as it arrives rather than read into
    /// memory: an artifact runs to hundreds of megabytes, and a tray holding one would be paged out
    /// before it finished. Returns the bytes written, and throws
    /// <see cref="ArtifactTooLargeException"/> past <paramref name="maxBytes"/>.
    /// <para>
    /// The provider composes the URL and nothing else. Where the file goes, and what it is safely
    /// called, is the caller's: several services report a path here rather than a name, and
    /// sanitising it once is the difference between one careful function and ten chances to write
    /// outside the directory.
    /// </para>
    /// </summary>
    Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel);

    /// <summary>
    /// Proves the credential works, and says who it belongs to and what it may do when the API tells.
    /// </summary>
    Task<ConnectionTest> Test(ProviderContext context, Cancel cancel);

    /// <summary>
    /// What the credential may do to builds, from its scopes or its user's rights, read without
    /// trying a change. Asked before each discovery, so a connection that can only watch offers no
    /// retry or cancel that would be refused after the click. <see cref="BuildAccess.Unknown"/> where
    /// the service does not say, or says in a way the answer cannot be trusted.
    /// </summary>
    Task<BuildAccess> Access(ProviderContext context, Cancel cancel);

    /// <summary>
    /// A cheap signal of recent activity: group key to a token that changes when the group has
    /// news, from one call or a few. A quiet group waits minutes between fetches; without a signal a
    /// push to it would too. Null when the provider has no such call. <paramref name="previous"/>
    /// holds the tokens seen last time, for a provider that asks only for what came after them.
    /// </summary>
    Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel);
}
