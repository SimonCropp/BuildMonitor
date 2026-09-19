/// <summary>
/// Where an artifact actually lives. <see cref="Properties"/> is an open bag rather than a typed
/// shape because that is how the API returns it, and the one entry wanted from it,
/// <c>artifactsize</c>, is not in the documented schema at all.
/// </summary>
class AzureDevOpsArtifactResource
{
    public string? Type { get; set; }
    public Dictionary<string, string>? Properties { get; set; }
}
