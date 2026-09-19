/// <summary>
/// One entry in a build's artifacts, which is either a file or a folder: a file carries
/// <see cref="Content"/> to fetch it by, a folder carries <see cref="Children"/> to list it by.
/// </summary>
class TeamCityArtifact
{
    public string Name { get; set; } = "";
    public long? Size { get; set; }
    public TeamCityHref? Content { get; set; }
    public TeamCityHref? Children { get; set; }
}
