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