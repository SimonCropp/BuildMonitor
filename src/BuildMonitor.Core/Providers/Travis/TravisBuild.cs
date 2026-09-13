class TravisBuild
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string State { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? PullRequestNumber { get; set; }
    public TravisBranch? Branch { get; set; }
    public TravisCommit? Commit { get; set; }
}