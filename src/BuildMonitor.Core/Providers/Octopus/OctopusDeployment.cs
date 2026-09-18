class OctopusDeployment
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string? ReleaseId { get; set; }
    public string? Name { get; set; }
    public OctopusLinks? Links { get; set; }
}