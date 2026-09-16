/// <summary>
/// The access token a request was made with, as personal_access_tokens/self describes it.
/// </summary>
class GitLabToken
{
    public List<string> Scopes { get; set; } = [];
    // A granular token holds its rights outside its scopes.
    public bool Granular { get; set; }
}
