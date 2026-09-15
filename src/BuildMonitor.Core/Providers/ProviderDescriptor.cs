/// <summary>
/// What the connection editor and the docs need to know about a provider without talking to it.
/// </summary>
/// <param name="TokenLabel">What the provider calls its credential: "API token", "Personal access token".</param>
/// <param name="TokenHelpUrl">Where a user creates one.</param>
/// <param name="UserLabel">Set when the scheme pairs the token with a user name, such as Jenkins
/// or Bitbucket; null when the token stands alone.</param>
/// <param name="CustomClientId">Whether a user may supply their own OAuth client id, which a self
/// hosted instance with its own application registration needs.</param>
/// <param name="ActionPermission">What a credential needs to retry and cancel, named in the
/// status when one is refused; null when the provider does not document it.</param>
/// <param name="FetchUnit">What one fetch covers, which the poller schedules as one group.</param>
/// <param name="FetchConcurrency">How many groups are fetched at once.</param>
/// <param name="Quota">A request budget the service enforces, or null when its headers are enough.</param>
/// <param name="IdleCap">The longest a quiet group waits between fetches; null for the default.</param>
/// <param name="ProbeInterval">How often <see cref="IProvider.RecentActivity"/> is asked; null for
/// the poll interval.</param>
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
    string? Notes = null,
    FetchUnit FetchUnit = FetchUnit.Pipeline,
    int FetchConcurrency = 1,
    RequestQuota? Quota = null,
    TimeSpan? IdleCap = null,
    TimeSpan? ProbeInterval = null,
    string? ActionPermission = null)
{
    /// <summary>
    /// The provider's page in the docs, which the connection editor links so the server and scope
    /// formats are one click away when a test fails.
    /// </summary>
    public string DocsUrl => $"https://github.com/SimonCropp/BuildMonitor/blob/main/docs/providers/{Id}.md";

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
