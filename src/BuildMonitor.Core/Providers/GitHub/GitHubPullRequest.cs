class GitHubPullRequest
{
    public long Number { get; set; }
    // Only a pull request asked for itself says these; a run's list of its pull requests does not.
    public string? State { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}
