/// <summary>
/// A project the user follows, as API v1.1 lists them. API v2 has no listing of projects at all.
/// </summary>
class CircleCiFollowedProject
{
    public string? VcsUrl { get; set; }
    // github or bitbucket, where a project slug says gh or bb.
    public string? VcsType { get; set; }
    public string Username { get; set; } = "";
    public string Reponame { get; set; } = "";
    public string? DefaultBranch { get; set; }
}
