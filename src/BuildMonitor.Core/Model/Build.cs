/// <summary>
/// One run of one pipeline, as every provider reports it. Immutable: a poll replaces the builds
/// of its connection wholesale rather than patching them.
/// </summary>
/// <param name="ProviderRef">
/// Whatever the provider needs to retry or cancel this run: a numeric build id, a task link, a
/// stage locator. Opaque to everything but the provider that wrote it.
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
    string ProviderRef)
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
