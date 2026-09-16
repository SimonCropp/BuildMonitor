/// <summary>
/// The OAuth token a request was made with, as oauth/token/info describes it.
/// </summary>
class GitLabTokenInfo
{
    public List<string> Scope { get; set; } = [];
}
