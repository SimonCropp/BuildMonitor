class GoCdDashboardPipeline
{
    public string Name { get; set; } = "";

    [JsonPropertyName("_embedded")]
    public GoCdDashboardPipelineEmbedded? Embedded { get; set; }
}