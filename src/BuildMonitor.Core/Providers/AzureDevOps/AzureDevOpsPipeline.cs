class AzureDevOpsPipeline
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Folder { get; set; }

    [JsonPropertyName("_links")]
    public AzureDevOpsLinks? Links { get; set; }
}