/// <summary>
/// A folder of projects on the server. Octopus deployments are grouped under the one their project
/// belongs to, so a family of projects deployed together reads as one row.
/// </summary>
class OctopusProjectGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
