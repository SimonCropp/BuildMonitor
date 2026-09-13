class AzureDevOpsTriggerInfo
{
    [JsonPropertyName("ci.message")]
    public string? Message { get; set; }
}