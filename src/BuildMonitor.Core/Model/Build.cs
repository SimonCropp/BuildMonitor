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
/// <param name="ProjectUrl">
/// The repository or project page the provider icon opens. Null when the provider has no page
/// above the build, and then clicking the icon does nothing.
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
    string? ProjectUrl = null)
{
    /// <summary>
    /// What a row is: a pipeline on a branch. Stable across polls so the selection survives a
    /// refresh, and the key durations are recorded against.
    /// </summary>
    public string Key => $"{ConnectionId}/{PipelineId}/{Branch}";

    public string PipelineKey => $"{ConnectionId}/{PipelineId}";

    public bool IsActive => Status is BuildStatus.Queued or BuildStatus.Running;

    public DateTimeOffset? Ordering => Started ?? Queued ?? Finished;
}
