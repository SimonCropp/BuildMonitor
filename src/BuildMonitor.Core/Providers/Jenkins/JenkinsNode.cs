class JenkinsNode
{
    [JsonPropertyName("_class")]
    public string? Class { get; init; }

    public string Name { get; init; } = "";
    public string? DisplayName { get; init; }
    public string Url { get; init; } = "";
    public List<JenkinsNode>? Jobs { get; init; }
    // Rises with every new build, even when old builds are discarded.
    public long? NextBuildNumber { get; init; }
    public bool InQueue { get; init; }
}