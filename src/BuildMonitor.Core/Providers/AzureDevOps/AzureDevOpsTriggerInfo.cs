class AzureDevOpsTriggerInfo
{
    [JsonPropertyName("ci.message")]
    public string? Message { get; set; }

    // A pull request build's own branch, where the build's parameters do not say it.
    [JsonPropertyName("pr.sourceBranch")]
    public string? SourceBranch { get; set; }

    // "True" for a pull request from a fork, whose branch name alone would read as this repository's.
    [JsonPropertyName("pr.isFork")]
    public string? IsFork { get; set; }
}
