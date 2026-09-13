class GitLabPipeline
{
    public long Id { get; set; }
    public long Iid { get; set; }
    public string Status { get; set; } = "";
    public string? Source { get; set; }
    public string? Ref { get; set; }
    public string? Sha { get; set; }
    public string? Name { get; set; }
    public string WebUrl { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}