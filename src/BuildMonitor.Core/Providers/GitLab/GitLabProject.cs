class GitLabProject
{
    public long Id { get; set; }
    public string PathWithNamespace { get; set; } = "";
    public string WebUrl { get; set; } = "";
    // In the simple listing too, so knowing it costs no request.
    public string? DefaultBranch { get; set; }
}