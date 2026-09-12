class TeamCityBuildTypes
{
    public List<TeamCityBuildType> BuildType { get; set; } = [];
}

class TeamCityBuildType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string WebUrl { get; set; } = "";
}

class TeamCityBuilds
{
    public List<TeamCityBuild> Build { get; set; } = [];
}

class TeamCityBuild
{
    public long Id { get; set; }
    public string? Number { get; set; }
    public string? Status { get; set; }
    public string? State { get; set; }
    public string? BranchName { get; set; }
    public bool? DefaultBranch { get; set; }
    public string WebUrl { get; set; } = "";
    public string? StatusText { get; set; }
    public string? QueuedDate { get; set; }
    public string? StartDate { get; set; }
    public string? FinishDate { get; set; }
    public string? BuildTypeId { get; set; }
    public TeamCityCanceledInfo? CanceledInfo { get; set; }

    [JsonPropertyName("running-info")]
    public TeamCityRunningInfo? RunningInfo { get; set; }

    public TeamCityTriggered? Triggered { get; set; }
    public TeamCityRevisions? Revisions { get; set; }
}

class TeamCityCanceledInfo
{
    public string? Text { get; set; }
}

class TeamCityRunningInfo
{
    public double? PercentageComplete { get; set; }
    public long? ElapsedSeconds { get; set; }
    public long? EstimatedTotalSeconds { get; set; }
    public long? LeftSeconds { get; set; }
}

class TeamCityTriggered
{
    public TeamCityUser? User { get; set; }
}

class TeamCityUser
{
    public string? Username { get; set; }
    public string? Name { get; set; }
}

class TeamCityRevisions
{
    public List<TeamCityRevision> Revision { get; set; } = [];
}

class TeamCityRevision
{
    public string? Version { get; set; }
}

class TeamCityServer
{
    public string? Version { get; set; }
}

record TeamCityQueueRequest(TeamCityBuildTypeReference BuildType, string? BranchName);

record TeamCityBuildTypeReference(string Id);

record TeamCityCancel(string Comment, bool ReaddIntoQueue);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TeamCityBuildTypes))]
[JsonSerializable(typeof(TeamCityBuilds))]
[JsonSerializable(typeof(TeamCityServer))]
[JsonSerializable(typeof(TeamCityQueueRequest))]
[JsonSerializable(typeof(TeamCityCancel))]
partial class TeamCityContext : JsonSerializerContext;
