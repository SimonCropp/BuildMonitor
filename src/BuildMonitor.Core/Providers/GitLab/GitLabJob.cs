class GitLabJob
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Stage { get; set; }
    public bool AllowFailure { get; set; }
    public GitLabArtifactsFile? ArtifactsFile { get; set; }

    /// <summary>
    /// When the archive stops being served. GitLab keeps reporting the file on the job after that,
    /// and answers a download for it with a 404.
    /// </summary>
    public DateTimeOffset? ArtifactsExpireAt { get; set; }
}
