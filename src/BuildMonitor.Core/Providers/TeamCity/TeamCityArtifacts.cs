/// <summary>
/// One level of a build's artifacts. The list is called <c>file</c> whether its entries are files
/// or folders.
/// </summary>
class TeamCityArtifacts
{
    public List<TeamCityArtifact> File { get; set; } = [];
}
