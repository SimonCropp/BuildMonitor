class GoCdBuildCause
{
    public string? TriggerMessage { get; set; }
    public List<GoCdMaterialRevision> MaterialRevisions { get; set; } = [];
}