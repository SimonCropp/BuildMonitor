class GoCdInstance
{
    public string Name { get; set; } = "";
    public long Counter { get; set; }
    public string? Label { get; set; }
    public long? ScheduledDate { get; set; }
    public GoCdBuildCause? BuildCause { get; set; }
    public List<GoCdStage> Stages { get; set; } = [];
}