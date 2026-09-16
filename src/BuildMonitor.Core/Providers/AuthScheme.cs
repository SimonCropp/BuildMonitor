/// <summary>
/// How the credential goes on the wire.
/// </summary>
public enum AuthScheme
{
    // Authorization: Bearer {token}. OAuth tokens, GitLab's and Azure DevOps' included.
    Bearer,
    // Authorization: token {token}. Travis.
    Token,
    // Authorization: Basic base64(user:token). Jenkins.
    BasicUserToken,
    // Authorization: Basic base64(:token). Azure DevOps personal access tokens. Its Microsoft Entra
    // tokens go as Bearer tokens, the way Microsoft's REST samples send them.
    BasicEmptyUserToken,
    // Authorization: Basic base64(email:token). Bitbucket API tokens.
    BasicEmailToken,
    // PRIVATE-TOKEN: {token}. GitLab personal, project and group access tokens, and never its OAuth
    // tokens, which GitLab does not look for here.
    HeaderPrivateToken,
    // X-Octopus-ApiKey: {token}.
    HeaderOctopusApiKey
}
