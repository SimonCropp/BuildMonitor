class GitLabProject
{
    public long Id { get; init; }
    public string PathWithNamespace { get; init; } = "";
    public string WebUrl { get; init; } = "";
    // In the simple listing too, so knowing it costs no request.
    public string? DefaultBranch { get; init; }
}