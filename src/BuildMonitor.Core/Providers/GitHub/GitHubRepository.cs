class GitHubRepository
{
    public string FullName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public bool Archived { get; set; }
    public bool Disabled { get; set; }
    public bool Fork { get; set; }
    public DateTimeOffset? PushedAt { get; set; }
    // In every listing, so knowing it costs no request.
    public string? DefaultBranch { get; set; }
    // The authenticated user's rights on the repository. Absent from a listing that does not say.
    public GitHubPermissions? Permissions { get; set; }
}