class OctopusTask
{
    public string Id { get; set; } = "";
    public string? State { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? QueueTime { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? CompletedTime { get; set; }
    public OctopusLinks? Links { get; set; }
}