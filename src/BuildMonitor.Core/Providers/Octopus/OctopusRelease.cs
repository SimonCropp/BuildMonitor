/// <summary>
/// A release, read only for its notes: what a pipeline that created it left about the run, such as
/// the user id of whoever it was created for.
/// </summary>
class OctopusRelease
{
    public string Id { get; set; } = "";
    public string? ReleaseNotes { get; set; }
}
