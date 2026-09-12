class GoCdDashboard
{
    [JsonPropertyName("_embedded")]
    public GoCdDashboardEmbedded? Embedded { get; set; }
}

class GoCdDashboardEmbedded
{
    public List<GoCdPipelineGroup> PipelineGroups { get; set; } = [];
}

class GoCdPipelineGroup
{
    public string Name { get; set; } = "";
    public List<string> Pipelines { get; set; } = [];
}

class GoCdHistory
{
    public List<GoCdInstance> Pipelines { get; set; } = [];
}

class GoCdInstance
{
    public string Name { get; set; } = "";
    public long Counter { get; set; }
    public string? Label { get; set; }
    public long? ScheduledDate { get; set; }
    public GoCdBuildCause? BuildCause { get; set; }
    public List<GoCdStage> Stages { get; set; } = [];
}

class GoCdBuildCause
{
    public string? TriggerMessage { get; set; }
    public List<GoCdMaterialRevision> MaterialRevisions { get; set; } = [];
}

class GoCdMaterialRevision
{
    public GoCdMaterial? Material { get; set; }
    public List<GoCdModification> Modifications { get; set; } = [];
}

class GoCdMaterial
{
    public string? Type { get; set; }
    public string? Description { get; set; }
}

class GoCdModification
{
    public string? Revision { get; set; }
    public string? Comment { get; set; }
    public string? UserName { get; set; }
}

class GoCdStage
{
    public string Name { get; set; } = "";
    public string? Counter { get; set; }
    public string? Status { get; set; }
    public string? Result { get; set; }
    public bool Scheduled { get; set; }
    public List<GoCdJob> Jobs { get; set; } = [];
}

class GoCdJob
{
    public string Name { get; set; } = "";
    public string? State { get; set; }
    public string? Result { get; set; }
    public long? ScheduledDate { get; set; }
}

class GoCdUser
{
    public string? LoginName { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GoCdDashboard))]
[JsonSerializable(typeof(GoCdHistory))]
[JsonSerializable(typeof(GoCdUser))]
partial class GoCdContext : JsonSerializerContext;
