/// <summary>
/// One archived file. Jenkins reports no size for an artifact anywhere in its remote API, so there
/// is nothing here to weigh a budget against.
/// </summary>
class JenkinsArtifact
{
    public string FileName { get; set; } = "";

    /// <summary>
    /// Where the file sits under the workspace, which is both how it is fetched and the only name
    /// that tells two artifacts of the same file name apart.
    /// </summary>
    public string RelativePath { get; set; } = "";
}
