class GitHubArtifact
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long SizeInBytes { get; set; }

    /// <summary>
    /// Past its retention. GitHub keeps listing one for a while after it stops serving it, and a
    /// download answers 410.
    /// </summary>
    public bool Expired { get; set; }
}
