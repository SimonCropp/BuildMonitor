/// <summary>
/// A build's parameters, as far as the ones asked for by name. Absent altogether from a build whose
/// parameters the credential may not read, which TeamCity answers by leaving the collection out
/// rather than sending an empty one.
/// </summary>
class TeamCityProperties
{
    public List<TeamCityProperty> Property { get; set; } = [];
}
