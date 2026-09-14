class GoCdDashboardInstance
{
    public long Counter { get; set; }

    [JsonPropertyName("_embedded")]
    public GoCdDashboardInstanceEmbedded? Embedded { get; set; }
}