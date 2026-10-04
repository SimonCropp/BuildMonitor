class GitHubPullRequest
{
    public long Number { get; init; }
    // Only a pull request asked for itself says these; a run's list of its pull requests does not.
    public string? State { get; init; }
    public DateTimeOffset? MergedAt { get; init; }
    public GitHubPullRequestHead? Head { get; init; }
}
