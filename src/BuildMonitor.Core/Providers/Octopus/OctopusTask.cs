class OctopusTask
{
    public string Id { get; init; } = "";
    public string? State { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? QueueTime { get; init; }
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? CompletedTime { get; init; }
    public OctopusLinks? Links { get; init; }
}