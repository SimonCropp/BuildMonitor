class AzureDevOpsTimelineRecord
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Result { get; set; }
    public AzureDevOpsLogReference? Log { get; set; }
}