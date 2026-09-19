/// <summary>
/// A job's artifact archive, as the job listing reports it. Listed with the job rather than
/// fetched, so a pipeline's artifacts cost no request beyond the one the log already makes.
/// </summary>
class GitLabArtifactsFile
{
    public string? Filename { get; set; }
    public long? Size { get; set; }
}
