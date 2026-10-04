record GitLabPipeline
{
    public long Id { get; init; }
    public long Iid { get; init; }
    public string Status { get; init; } = "";
    public string? Source { get; init; }
    public string? Ref { get; init; }
    public string? Sha { get; init; }
    public string? Name { get; init; }
    public string WebUrl { get; init; } = "";
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
}