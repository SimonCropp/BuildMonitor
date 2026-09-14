/// <summary>
/// A GraphQL answer. GraphQL reports failures as a 200 carrying errors, which the HTTP layer
/// cannot see, so the caller has to look.
/// </summary>
class GitLabGraphResponse
{
    public GitLabGraphData? Data { get; set; }
    public List<GitLabGraphError>? Errors { get; set; }
}