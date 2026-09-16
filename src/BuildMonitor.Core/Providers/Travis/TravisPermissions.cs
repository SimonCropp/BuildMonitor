/// <summary>
/// A build's @permissions: the checks the server makes when the same user asks it to cancel or
/// restart the build.
/// </summary>
class TravisPermissions
{
    public bool? Cancel { get; set; }
    public bool? Restart { get; set; }
}
