/// <summary>
/// What is kept of a run between polls: the fields a build is made from, flattened. The parsed run
/// holds its actor, head commit, head repository and pull requests as objects of their own, and
/// <see cref="ETagCache"/> keeps every page of runs for the 304s that hand it back, so a large
/// account held thousands of those graphs for the few strings each build reads from them: 3,397
/// runs in one tray. Over 400 runs of four public repositories, a run held 755 bytes as parsed and
/// 547 as summarized, strings included.
/// <para>
/// Immutable, since a 304 hands the same summary to every poll that asks for its page.
/// </para>
/// </summary>
sealed class GitHubRunSummary(GitHubRun run)
{
    public long Id { get; } = run.Id;
    public long WorkflowId { get; } = run.WorkflowId;
    public long RunNumber { get; } = run.RunNumber;
    public string Status { get; } = run.Status;
    public string? Conclusion { get; } = run.Conclusion;
    public string? Event { get; } = run.Event;
    public string? HeadBranch { get; } = run.HeadBranch;
    public string? HeadSha { get; } = run.HeadSha;

    /// <summary>
    /// The head commit's whole message, or the run's title where it names no commit.
    /// </summary>
    public string? Message { get; } = run.HeadCommit?.Message ?? run.DisplayTitle;

    public string HtmlUrl { get; } = run.HtmlUrl;

    /// <summary>
    /// When this attempt was raised. created_at is the first attempt's and a re-run keeps it, so a
    /// run retried in the morning read "queued 10h", counted from its first run the night before.
    /// run_started_at is reset by each attempt and is created_at on the first. The run gives no
    /// time a runner picked it up, so the attempt is its start as well.
    /// </summary>
    public DateTimeOffset? Started { get; } = run.RunStartedAt ?? run.CreatedAt;

    public DateTimeOffset? UpdatedAt { get; } = run.UpdatedAt;
    public string? Actor { get; } = run.Actor?.Login;

    /// <summary>
    /// The repository the branch is in: a fork for a pull request from one, not the run's repository.
    /// </summary>
    public string? HeadRepository { get; } = run.HeadRepository?.FullName;

    public string? HeadRepositoryUrl { get; } = run.HeadRepository?.HtmlUrl;
    public long? PullRequest { get; } = run.PullRequests?.FirstOrDefault()?.Number;
}
