class GitHubRun
{
    public long Id { get; set; }
    public long WorkflowId { get; set; }
    public long RunNumber { get; set; }
    public string Status { get; set; } = "";
    public string? Conclusion { get; set; }
    public string? HeadBranch { get; set; }
    public string? HeadSha { get; set; }
    public string? DisplayTitle { get; set; }
    public string HtmlUrl { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? RunStartedAt { get; set; }
    public GitHubActor? Actor { get; set; }
    public GitHubCommit? HeadCommit { get; set; }
    public List<GitHubPullRequest> PullRequests { get; set; } = [];
}