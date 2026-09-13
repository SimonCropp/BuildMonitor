class AzureDevOpsBuild
{
    public long Id { get; set; }
    public string? BuildNumber { get; set; }
    public string? Status { get; set; }
    public string? Result { get; set; }
    public DateTimeOffset? QueueTime { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? FinishTime { get; set; }
    public string? SourceBranch { get; set; }
    public string? SourceVersion { get; set; }
    public string? Reason { get; set; }
    public AzureDevOpsIdentity? RequestedFor { get; set; }
    public AzureDevOpsDefinition? Definition { get; set; }
    public AzureDevOpsRepository? Repository { get; set; }
    public AzureDevOpsTriggerInfo? TriggerInfo { get; set; }

    [JsonPropertyName("_links")]
    public AzureDevOpsLinks? Links { get; set; }
}