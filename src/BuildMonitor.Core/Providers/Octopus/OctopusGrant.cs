/// <summary>
/// Where one grant of a permission applies. A list holding the sentinel id for its kind, such as
/// <c>projects-all</c>, is no restriction of that kind, and neither is an empty one. Octopus
/// expands a grant scoped to a project group into the projects in it, so the group ids never say
/// what a grant reaches.
/// </summary>
class OctopusGrant
{
    public string? SpaceId { get; set; }
    public List<string>? RestrictedToProjectIds { get; set; }
    public List<string>? RestrictedToEnvironmentIds { get; set; }
    public List<string>? RestrictedToTenantIds { get; set; }
    public List<string>? RestrictedToProjectGroupIds { get; set; }
}
