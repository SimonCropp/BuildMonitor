/// <summary>
/// What a user may do, as users/{id}/permissions answers for the spaces asked about: each space
/// permission with every grant of it.
/// </summary>
class OctopusPermissions
{
    public Dictionary<string, List<OctopusGrant>>? SpacePermissions { get; init; }
    public List<string>? SystemPermissions { get; init; }
    // False when the server could not show everything that grants the user a permission.
    public bool IsPermissionsComplete { get; init; }
}
