class OctopusPage<T>
{
    public List<T> Items { get; set; } = [];
}

class OctopusSpace
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
}

class OctopusProject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public OctopusLinks? Links { get; set; }
}

class OctopusEnvironment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

class OctopusDeployment
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string? Name { get; set; }
    public OctopusLinks? Links { get; set; }
}

class OctopusTask
{
    public string Id { get; set; } = "";
    public string? State { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? QueueTime { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? CompletedTime { get; set; }
    public OctopusLinks? Links { get; set; }
}

class OctopusTaskDetails
{
    public OctopusProgress? Progress { get; set; }
}

class OctopusProgress
{
    public double? ProgressPercentage { get; set; }
    public string? EstimatedTimeRemaining { get; set; }
}

class OctopusLinks
{
    public string? Web { get; set; }
    public string? Details { get; set; }
    public string? Rerun { get; set; }
    public string? Cancel { get; set; }
}

class OctopusUser
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OctopusPage<OctopusSpace>))]
[JsonSerializable(typeof(OctopusPage<OctopusProject>))]
[JsonSerializable(typeof(OctopusPage<OctopusDeployment>))]
[JsonSerializable(typeof(OctopusPage<OctopusTask>))]
[JsonSerializable(typeof(List<OctopusEnvironment>))]
[JsonSerializable(typeof(OctopusTask))]
[JsonSerializable(typeof(OctopusTaskDetails))]
[JsonSerializable(typeof(OctopusUser))]
partial class OctopusContext : JsonSerializerContext;
