/// <summary>
/// A link the API hands back, server root absolute. Resolving it against the connection's address
/// replaces the whole path, which is what makes these usable as they arrive.
/// </summary>
class TeamCityHref
{
    public string Href { get; set; } = "";
}
