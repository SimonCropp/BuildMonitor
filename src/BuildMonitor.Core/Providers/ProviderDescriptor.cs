/// <summary>
/// What the connection editor and the docs need to know about a provider without talking to it.
/// </summary>
/// <param name="PipelineNoun">What the provider calls the thing that produces builds: an action,
/// a job, a build configuration. Named wherever a pipeline is acted on by name, so "Exclude CI"
/// cannot read as excluding something other than the pipeline it names.</param>
/// <param name="OrgNoun">What the provider calls the account a repository sits under: an org, a
/// workspace, a group. Null for a provider with nothing above the repository, and then its rows
/// offer no org to exclude, however many separators the repository name happens to carry: a
/// Jenkins folder path is not an account.</param>
/// <param name="TokenLabel">What the provider calls its credential: "API token", "Personal access token".</param>
/// <param name="TokenHelpUrl">Where a user creates one.</param>
/// <param name="Scheme">How a token the user pastes goes on the wire.</param>
/// <param name="UserLabel">Set when the scheme pairs the token with a user name, such as Jenkins
/// or Bitbucket; null when the token stands alone.</param>
/// <param name="CustomClientId">Whether a user may supply their own OAuth client id, which a self
/// hosted instance with its own application registration needs.</param>
/// <param name="HasQueuePriority">Whether the service can move a build already in its queue to the
/// front of it. Most cannot: a queue that only runs in the order it was filled has nothing to ask,
/// and a row on such a service offers no Run next rather than one that would be refused. Asked
/// before the chip is drawn, as <paramref name="HasArtifacts"/> is.</param>
/// <param name="ActionPermission">What a credential needs to retry and cancel, named in the
/// status when one is refused; null when the provider does not document it.</param>
/// <param name="QueuePermission">What a credential needs to move a queued build to the front,
/// where that is a permission apart from <paramref name="ActionPermission"/>. A connection that can
/// cancel a build can still be refused this, and the retry and cancel message then blamed a token
/// scope the connection already had.</param>
/// <param name="FetchUnit">What one fetch covers, which the poller schedules as one group.</param>
/// <param name="FetchConcurrency">How many groups are fetched at once. A cycle applies its rows only
/// once every due group is back, so one at a time left a first poll of a few hundred jobs showing
/// nothing for most of a minute, and a Refresh showing stale rows for as long.</param>
/// <param name="Quota">A request budget the service enforces, or null when its headers are enough.</param>
/// <param name="IdleCap">The longest a quiet group waits between fetches; null for the default.</param>
/// <param name="ProbeInterval">How often <see cref="IProvider.RecentActivity"/> is asked; null for
/// the poll interval.</param>
/// <param name="SignInScheme">How a token from the browser or device sign in goes on the wire,
/// where that differs from <paramref name="Scheme"/>; null where it does not.</param>
/// <param name="TokenNote">Where the user creates a token and what it has to be allowed to do,
/// shown only while Token is the chosen method. Named for the method it belongs to rather than as
/// notes in general, because every one of these is about a token a user pastes and none of it holds
/// for a browser or device sign in, where the provider's own consent screen grants the scopes.</param>
/// <param name="SignInNote">Which accounts the browser and device flows turn away, shown only while
/// one of them is the chosen method. Said here because the provider's own sign in page says it as a
/// rejected address rather than as a reason, which reads as the address being wrong.</param>
record ProviderDescriptor(
    string Id,
    string Name,
    string PipelineNoun,
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
    bool HasArtifacts,
    bool HasQueuePriority = false,
    string? OrgNoun = null,
    bool CustomClientId = false,
    string? TokenNote = null,
    FetchUnit FetchUnit = FetchUnit.Pipeline,
    int FetchConcurrency = 1,
    RequestQuota? Quota = null,
    TimeSpan? IdleCap = null,
    TimeSpan? ProbeInterval = null,
    string? ActionPermission = null,
    string? QueuePermission = null,
    AuthScheme? SignInScheme = null,
    string? SignInNote = null)
{
    /// <summary>
    /// The provider's page in the docs, which the connection editor links so the server and scope
    /// formats are one click away when a test fails.
    /// </summary>
    public string DocsUrl => $"https://github.com/SimonCropp/BuildMonitor/blob/main/docs/providers/{Id}.md";

    /// <summary>
    /// How the credential of a connection authenticated by <paramref name="method"/> goes on the
    /// wire. The one place that decides it, for polls, actions, tests and sign ins alike. GitLab
    /// looks for an access token in PRIVATE-TOKEN but for an OAuth token only in the Authorization
    /// header, so one scheme for both had every request of a signed in connection refused.
    /// </summary>
    public AuthScheme SchemeFor(AuthMethod method)
    {
        if (method != AuthMethod.Token &&
            SignInScheme is { } signIn)
        {
            return signIn;
        }

        return Scheme;
    }

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
