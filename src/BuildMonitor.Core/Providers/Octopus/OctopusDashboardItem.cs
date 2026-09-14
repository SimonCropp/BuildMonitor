/// <summary>
/// The current or previous deployment of one project to one environment.
/// </summary>
class OctopusDashboardItem
{
    public string ProjectId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string DeploymentId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string? ReleaseVersion { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? QueueTime { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? CompletedTime { get; set; }
}