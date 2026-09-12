class AzureDevOpsList<T>
{
    public List<T> Value { get; set; } = [];
}

class AzureDevOpsProject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

class AzureDevOpsPipeline
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Folder { get; set; }

    [JsonPropertyName("_links")]
    public AzureDevOpsLinks? Links { get; set; }
}

class AzureDevOpsLinks
{
    public AzureDevOpsLink? Web { get; set; }
}

class AzureDevOpsLink
{
    public string Href { get; set; } = "";
}

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

class AzureDevOpsIdentity
{
    public string? DisplayName { get; set; }
}

class AzureDevOpsDefinition
{
    public long Id { get; set; }
    public string? Name { get; set; }
}

class AzureDevOpsRepository
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
}

class AzureDevOpsTriggerInfo
{
    [JsonPropertyName("ci.message")]
    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsProject>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsPipeline>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsBuild>))]
partial class AzureDevOpsContext : JsonSerializerContext;
