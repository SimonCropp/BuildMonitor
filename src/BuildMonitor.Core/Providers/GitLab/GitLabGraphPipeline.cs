class GitLabGraphPipeline
{
    // A global id: gid://gitlab/Ci::Pipeline/5001.
    public string Id { get; set; } = "";
    public string Iid { get; set; } = "";
    // Upper case: RUNNING, WAITING_FOR_RESOURCE.
    public string Status { get; set; } = "";
    public string? Ref { get; set; }
    public string? Sha { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public GitLabGraphUser? User { get; set; }
}