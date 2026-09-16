class GoCdDashboardPipeline
{
    public string Name { get; set; } = "";

    // Whether the user may operate the pipeline's group, which pausing needs and so does every
    // change the API makes.
    public bool? CanPause { get; set; }

    // Whether the user may operate the pipeline's first stage, which scheduling it needs.
    public bool? CanOperate { get; set; }

    [JsonPropertyName("_embedded")]
    public GoCdDashboardPipelineEmbedded? Embedded { get; set; }
}