class GoCdInstance
{
    public string Name { get; init; } = "";
    public long Counter { get; init; }
    public string? Label { get; init; }
    public long? ScheduledDate { get; init; }
    public GoCdBuildCause? BuildCause { get; init; }
    public List<GoCdStage>? Stages { get; set; }
}