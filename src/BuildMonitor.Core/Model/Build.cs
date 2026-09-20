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
/// know it, and then the name is plain text rather than a link to something else. Kept apart from
/// <paramref name="PipelineUrl"/> because one field for both had every provider choose which it
/// meant: an AppVeyor logo opened github.com while a Jenkins one opened Jenkins.
/// </param>
/// <param name="PipelineUrl">
/// The pipeline's own page on the CI service, which the provider's icon opens: the AppVeyor
/// project, the Jenkins job, the Actions workflow. Always known, since it is the page every
/// provider already builds its build URLs under.
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
    string? RepoUrl = null)
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

    public bool IsActive => Status is BuildStatus.Queued or BuildStatus.Running;

    public DateTimeOffset? Ordering => Started ?? Queued ?? Finished;
}
