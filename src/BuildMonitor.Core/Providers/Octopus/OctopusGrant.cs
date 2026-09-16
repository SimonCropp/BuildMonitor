/// <summary>
/// Where one grant of a permission applies. An empty list is no restriction of that kind.
/// </summary>
class OctopusGrant
{
    public string? SpaceId { get; set; }
    public List<string>? RestrictedToProjectIds { get; set; }
    public List<string>? RestrictedToEnvironmentIds { get; set; }
    public List<string>? RestrictedToTenantIds { get; set; }
    public List<string>? RestrictedToProjectGroupIds { get; set; }
}
