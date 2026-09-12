/// <summary>
/// One OAuth application registration, as the flows need it.
/// </summary>
/// <param name="ClientSecret">Only GitHub's web flow needs one; it is embedded the way the
/// GitHub CLI embeds its own, a secret in name only. Null disables the browser flow and leaves
/// the device flow, which needs none.</param>
/// <param name="RedirectTemplate">With a {port} placeholder for the loopback listener.</param>
record OAuthClient(
    string ProviderId,
    string AuthorizeUrl,
    string TokenUrl,
    string? DeviceCodeUrl,
    string ClientId,
    string? ClientSecret,
    IReadOnlyList<string> Scopes,
    string RedirectTemplate,
    bool Pkce,
    IReadOnlyDictionary<string, string>? ExtraAuthorizeParameters = null)
{
    public string Scope => string.Join(' ', Scopes);

    public string RedirectUri(int port) =>
        RedirectTemplate.Replace("{port}", port.ToString());
}
