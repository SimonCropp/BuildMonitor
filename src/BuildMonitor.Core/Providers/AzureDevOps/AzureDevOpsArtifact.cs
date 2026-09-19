class AzureDevOpsArtifact
{
    public string Name { get; set; } = "";
    public AzureDevOpsArtifactResource? Resource { get; set; }

    /// <summary>
    /// The size the resource declared, or null where it declared none. Reported as a string of
    /// bytes under a property the schema does not document, so anything that will not parse is
    /// treated as no size rather than as zero.
    /// </summary>
    public long? Bytes()
    {
        if (Resource?.Properties is { } properties &&
            properties.TryGetValue("artifactsize", out var text) &&
            long.TryParse(text, CultureInfo.InvariantCulture, out var bytes))
        {
            return bytes;
        }

        return null;
    }
}
