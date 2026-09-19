/// <summary>
/// One file a job published. <see cref="FileName"/> is both the label and what the download asks
/// for, and can hold slashes where the job published into folders.
/// </summary>
class AppVeyorArtifact
{
    public string FileName { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public long? Size { get; set; }
}
