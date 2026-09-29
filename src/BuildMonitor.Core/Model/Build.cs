/// <summary>
/// One run of one pipeline, as every provider reports it. Immutable: a poll replaces the builds
/// of its connection wholesale rather than patching them.
/// </summary>
/// <param name="CanRetry">
/// Whether this connection may retry the run: its state allows it and, where the service says,
/// so do the credential and its user's rights. Every surface that offers a retry reads this.
/// </param>
/// <param name="CanCancel">
/// Whether this connection may cancel the run, on the same terms as <paramref name="CanRetry"/>.
/// </param>
/// <param name="ProviderRef">
/// Whatever the provider needs to retry or cancel this run: a numeric build id, a task link, a
/// stage locator. Opaque to everything but the provider that wrote it.
/// </param>
/// <param name="RepoUrl">
/// The source repository's page, which the row's name opens. Null where the provider does not
/// know it, as an Octopus deployment has none, and then the row
/// <see cref="BuildExtensions.LeadsWithRun">leads with its run</see> instead. Kept apart from
/// <paramref name="PipelineUrl"/> because one field for both had every provider choose which it
/// meant: an AppVeyor logo opened github.com while a Jenkins one opened Jenkins.
/// </param>
/// <param name="PipelineUrl">
/// The pipeline's own page on the CI service, which the provider's icon opens: the AppVeyor
/// project, the Jenkins job, the Actions workflow. Always known, since it is the page every
/// provider already builds its build URLs under.
/// </param>
/// <param name="ProjectGroup">
/// What the service files the pipeline under, carried from <see cref="Pipeline.ProjectGroup"/>:
/// passing builds sharing one are grouped under it ahead of the repository name. Null for every
/// provider that has no such thing, and then the repository name groups them as before.
/// </param>
/// <param name="DefaultBranch">
/// The pipeline's default branch, carried from <see cref="Pipeline.DefaultBranch"/> or worked out
/// by the provider from the runs it fetched, in the form of <paramref name="Branch"/>, so a run is
/// on it when the two are equal. Every build of one pipeline in one fetch carries the same one.
/// Null where the service has no such thing: GoCD, Octopus Deploy and Jenkins.
/// </param>
/// <param name="Project">
/// The level above the repository where the service keeps it out of the repository's name: the
/// Azure DevOps project, whose repositories are named on their own. Without it grouping by org
/// found no owner in an Azure DevOps name and left every repository in a group of its own. Null
/// for every other provider, whose repository name already carries its owner.
/// </param>
/// <param name="Folder">
/// The folder the pipeline is filed in, carried from <see cref="Pipeline.Folder"/> for the row's
/// hover. Null at the root and for every provider without folders.
/// </param>
record Build(
    string ConnectionId,
    string PipelineId,
    string PipelineName,
    string RepoName,
    string? Branch,
    string RunNumber,
    BuildStatus Status,
    string? StatusText,
    DateTimeOffset? Queued,
    DateTimeOffset? Started,
    DateTimeOffset? Finished,
    ProviderEstimate? Estimate,
    string BuildUrl,
    string? BranchUrl,
    string? PullRequestNumber,
    string? PullRequestUrl,
    string? CommitSha,
    string? CommitMessage,
    string? Author,
    bool CanRetry,
    bool CanCancel,
    string ProviderRef,
    string PipelineUrl,
    string? RepoUrl = null,
    string? ProjectGroup = null,
    string? DefaultBranch = null,
    string? Project = null,
    string? Folder = null)
{
    /// <summary>
    /// What a row is: a pipeline on a branch. Stable across polls so the selection survives a
    /// refresh, and the key durations are recorded against. Built on every read, so a comparison
    /// goes through <see cref="HasKey"/> or <see cref="SameKey"/>, which build nothing.
    /// </summary>
    public string Key => $"{ConnectionId}/{PipelineId}/{Branch}";

    /// <summary>
    /// Built on every read, like <see cref="Key"/>. Counting each pipeline's runs by comparing
    /// this built 1.7 million strings for one MCP pipelines call on a 588 pipeline account, so a
    /// comparison goes through <see cref="HasPipelineKey"/> or <see cref="SamePipeline"/>.
    /// </summary>
    public string PipelineKey => $"{ConnectionId}/{PipelineId}";

    /// <summary>
    /// Whether <paramref name="key"/> is <see cref="Key"/>, compared a part at a time against where
    /// each part would sit in it.
    /// </summary>
    public bool HasKey(string? key)
    {
        var branch = Branch ?? "";
        if (key is null ||
            key.Length != ConnectionId.Length + PipelineId.Length + branch.Length + 2 ||
            !key.EndsWith(branch, StringComparison.Ordinal))
        {
            return false;
        }

        var pipeline = key.AsSpan(0, key.Length - branch.Length - 1);
        return key[pipeline.Length] == '/' &&
               HasPipelineKey(pipeline);
    }

    /// <summary>
    /// Whether <paramref name="key"/> is <see cref="PipelineKey"/>, compared a part at a time.
    /// </summary>
    public bool HasPipelineKey(CharSpan key) =>
        key.Length == ConnectionId.Length + PipelineId.Length + 1 &&
        key.StartsWith(ConnectionId, StringComparison.Ordinal) &&
        key[ConnectionId.Length] == '/' &&
        key.EndsWith(PipelineId, StringComparison.Ordinal);

    /// <summary>
    /// Whether the two have the same <see cref="Key"/>, from the parts. A missing branch keys as an
    /// empty one, as it does in the key.
    /// </summary>
    public bool SameKey(Build other) =>
        SamePipeline(other) &&
        (Branch ?? "") == (other.Branch ?? "");

    public bool SamePipeline(Build other) =>
        ConnectionId == other.ConnectionId &&
        PipelineId == other.PipelineId;

    /// <summary>
    /// Orders builds as their <see cref="Key"/>s order, for a sort that ends on the key. A sort
    /// reads the key of everything it sorts before comparing any, so ending on the key itself built
    /// one for every row of every projection, to settle a tie almost none of them were in.
    /// </summary>
    public static readonly IComparer<Build> ByKey = Comparer<Build>.Create((left, right) => left.CompareKey(right));

    /// <summary>
    /// How <see cref="Key"/> compares to <paramref name="other"/>'s, ordinally. From the parts
    /// where they line up, which is wherever the two connections and the two pipelines are as long
    /// as each other; a shorter part puts its separator against the other's next character, and
    /// then the keys themselves are compared.
    /// </summary>
    public int CompareKey(Build other)
    {
        if (ConnectionId.Length != other.ConnectionId.Length ||
            PipelineId.Length != other.PipelineId.Length)
        {
            return string.CompareOrdinal(Key, other.Key);
        }

        var connection = string.CompareOrdinal(ConnectionId, other.ConnectionId);
        if (connection != 0)
        {
            return connection;
        }

        var pipeline = string.CompareOrdinal(PipelineId, other.PipelineId);
        if (pipeline != 0)
        {
            return pipeline;
        }

        return string.CompareOrdinal(Branch ?? "", other.Branch ?? "");
    }

    public bool IsActive => Status is BuildStatus.Queued or BuildStatus.Running;

    public DateTimeOffset? Ordering => Started ?? Queued ?? Finished;
}
