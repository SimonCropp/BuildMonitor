/// <summary>
/// What the connection editor and the docs need to know about a provider without talking to it.
/// </summary>
/// <param name="TokenLabel">What the provider calls its credential: "API token", "Personal access token".</param>
/// <param name="TokenHelpUrl">Where a user creates one.</param>
/// <param name="UserLabel">Set when the scheme pairs the token with a user name, such as Jenkins
/// or Bitbucket; null when the token stands alone.</param>
/// <param name="CustomClientId">Whether a user may supply their own OAuth client id, which a self
/// hosted instance with its own application registration needs.</param>
record ProviderDescriptor(
    string Id,
    string Name,
    bool BrowserSignIn,
    bool DeviceSignIn,
    bool SelfHosted,
    string? DefaultServer,
    string TokenLabel,
    string TokenHelpUrl,
    AuthScheme Scheme,
    string? UserLabel,
    IReadOnlyList<ScopeField> Scopes,
    bool HasEstimate,
    bool HasBranches,
    bool HasPullRequests,
    bool CustomClientId = false,
    string? Notes = null)
{
    public IEnumerable<AuthMethod> AuthMethods()
    {
        if (BrowserSignIn)
        {
            yield return AuthMethod.Browser;
        }

        if (DeviceSignIn)
        {
            yield return AuthMethod.Device;
        }

        yield return AuthMethod.Token;
    }
}
