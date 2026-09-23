class AppVeyorBuild
{
    public long BuildId { get; set; }
    public long BuildNumber { get; set; }
    public string Version { get; set; } = "";
    public string? Branch { get; set; }
    public string? CommitId { get; set; }
    public string? Message { get; set; }
    public string? AuthorName { get; set; }
    public string? PullRequestId { get; set; }
    // A pull request build's Branch is the branch it targets; these say where it came from.
    public string? PullRequestHeadBranch { get; set; }
    public string? PullRequestHeadRepository { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset? Created { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Finished { get; set; }
    public DateTimeOffset? Updated { get; set; }
    // Only in a single build's detail; the history leaves them out.
    public List<AppVeyorJob> Jobs { get; set; } = [];
}