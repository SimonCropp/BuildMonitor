class JenkinsNode
{
    [JsonPropertyName("_class")]
    public string? Class { get; set; }

    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Url { get; set; } = "";
    public string? Color { get; set; }
    public List<JenkinsNode>? Jobs { get; set; }
}

class JenkinsJob
{
    public List<JenkinsBuild> Builds { get; set; } = [];
    public bool InQueue { get; set; }
    public JenkinsQueueItem? QueueItem { get; set; }
}

class JenkinsQueueItem
{
    public long Id { get; set; }
    public long? InQueueSince { get; set; }
}

class JenkinsBuild
{
    public long Number { get; set; }
    public string Url { get; set; } = "";
    public string? Result { get; set; }
    public bool Building { get; set; }
    public long? Timestamp { get; set; }
    public long? Duration { get; set; }
    public long? EstimatedDuration { get; set; }
    public List<JenkinsAction>? Actions { get; set; }
}

class JenkinsAction
{
    public JenkinsRevision? LastBuiltRevision { get; set; }
}

class JenkinsRevision
{
    public List<JenkinsBranch>? Branch { get; set; }
}

class JenkinsBranch
{
    public string? Name { get; set; }
}

class JenkinsCrumb
{
    public string CrumbRequestField { get; set; } = "Jenkins-Crumb";
    public string Crumb { get; set; } = "";
}

class JenkinsUser
{
    public string? Id { get; set; }
    public string? FullName { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JenkinsNode))]
[JsonSerializable(typeof(JenkinsJob))]
[JsonSerializable(typeof(JenkinsCrumb))]
[JsonSerializable(typeof(JenkinsUser))]
partial class JenkinsContext : JsonSerializerContext;
