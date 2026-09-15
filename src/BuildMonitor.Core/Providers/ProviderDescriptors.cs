/// <summary>
/// The static half of every provider, in one place so the connection editor and the docs can be
/// built without an HTTP client. Each provider implementation exposes its own entry as
/// <see cref="IProvider.Descriptor"/>.
/// </summary>
static class ProviderDescriptors
{
    public static readonly ProviderDescriptor AppVeyor = new(
        Id: "appveyor",
        Name: "AppVeyor",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: false,
        DefaultServer: "https://ci.appveyor.com",
        TokenLabel: "API token",
        TokenHelpUrl: "https://ci.appveyor.com/api-keys",
        Scheme: AuthScheme.Bearer,
        UserLabel: null,
        Scopes:
        [
            new("account", "Account", Required: false, Hint: "Only needed for a v2 (user level) token")
        ],
        HasEstimate: false,
        HasBranches: true,
        HasPullRequests: true,
        FetchConcurrency: Concurrently.Limit,
        // The probe fetches a project whose latest build changed at once, so a quiet one need not be
        // fetched every five minutes, a full response each time.
        IdleCap: TimeSpan.FromMinutes(30),
        // No ETags: every probe is a full list, so once a minute rather than every poll interval.
        ProbeInterval: TimeSpan.FromMinutes(1));

    public static readonly ProviderDescriptor Travis = new(
        Id: "travis",
        Name: "Travis CI",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: true,
        DefaultServer: "https://api.travis-ci.com",
        TokenLabel: "API token",
        TokenHelpUrl: "https://app.travis-ci.com/account/preferences",
        Scheme: AuthScheme.Token,
        UserLabel: null,
        Scopes: [],
        HasEstimate: false,
        HasBranches: true,
        HasPullRequests: true,
        FetchConcurrency: Concurrently.Limit,
        // The probe fetches a repository whose last started build changed at once, so a quiet one
        // need not be fetched every five minutes, a full response each time.
        IdleCap: TimeSpan.FromMinutes(30),
        // No ETags: every probe is a full list, so once a minute rather than every poll interval.
        ProbeInterval: TimeSpan.FromMinutes(1));

    public static readonly ProviderDescriptor Jenkins = new(
        Id: "jenkins",
        Name: "Jenkins",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: true,
        DefaultServer: null,
        TokenLabel: "API token",
        TokenHelpUrl: "https://www.jenkins.io/doc/book/system-administration/authenticating-scripted-clients/",
        Scheme: AuthScheme.BasicUserToken,
        UserLabel: "User name",
        Scopes: [],
        HasEstimate: true,
        HasBranches: true,
        HasPullRequests: false,
        Notes: "Create the token at {server}/me/security.",
        // Fewer than the hosted services get: a self hosted server may be a small one.
        FetchConcurrency: 4,
        // The probe reads every discovered job's next build number in one request, so a quiet job
        // need not be fetched every five minutes, which reads its last builds from disk.
        IdleCap: TimeSpan.FromMinutes(30));

    public static readonly ProviderDescriptor GitHub = new(
        Id: "github",
        Name: "GitHub Actions",
        BrowserSignIn: true,
        DeviceSignIn: true,
        SelfHosted: true,
        DefaultServer: "https://api.github.com",
        TokenLabel: "Personal access token",
        TokenHelpUrl: "https://github.com/settings/personal-access-tokens/new",
        Scheme: AuthScheme.Bearer,
        UserLabel: null,
        Scopes:
        [
            new("owner", "Organization or user", Required: false, Hint: "Leave empty for every repository you can see")
        ],
        HasEstimate: false,
        HasBranches: true,
        HasPullRequests: true,
        Notes: "A fine grained token needs Actions read and write and Metadata read; a classic token needs the repo scope.",
        FetchUnit: FetchUnit.Repository,
        FetchConcurrency: Concurrently.Limit,
        // Half the secondary limit of 900 points a minute, which counts a 304 like any GET.
        Quota: new(450, TimeSpan.FromMinutes(1), 450),
        ActionPermission: "Actions read and write, or the repo scope on a classic token");

    public static readonly ProviderDescriptor AzureDevOps = new(
        Id: "azure-devops",
        Name: "Azure DevOps",
        BrowserSignIn: true,
        DeviceSignIn: true,
        SelfHosted: true,
        DefaultServer: "https://dev.azure.com",
        TokenLabel: "Personal access token",
        TokenHelpUrl: "https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate",
        Scheme: AuthScheme.BasicEmptyUserToken,
        UserLabel: null,
        Scopes:
        [
            new("organization", "Organization", Required: true),
            new("project", "Project", Required: false, Hint: "Leave empty for every project in the organization")
        ],
        HasEstimate: false,
        HasBranches: true,
        HasPullRequests: true,
        Notes: "The token needs Build (Read & execute).",
        FetchUnit: FetchUnit.Repository,
        FetchConcurrency: Concurrently.Limit,
        // Half the 200 throughput units a user may spend in any five minutes.
        Quota: new(100, TimeSpan.FromMinutes(5), 100, ChargeByCost: true),
        // The probe asks each project for builds queued since the newest one seen, so a quiet
        // project need not be fetched every five minutes.
        IdleCap: TimeSpan.FromMinutes(30),
        ActionPermission: "Build (Read & execute)");

    public static readonly ProviderDescriptor TeamCity = new(
        Id: "teamcity",
        Name: "TeamCity",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: true,
        DefaultServer: null,
        TokenLabel: "Access token",
        TokenHelpUrl: "https://www.jetbrains.com/help/teamcity/configuring-your-user-profile.html#Managing+Access+Tokens",
        Scheme: AuthScheme.Bearer,
        UserLabel: null,
        Scopes:
        [
            new("project", "Project id", Required: false, Hint: "Leave empty for every project")
        ],
        HasEstimate: true,
        HasBranches: true,
        HasPullRequests: false,
        Notes: "Create the token under Profile, Access Tokens.",
        FetchUnit: FetchUnit.Group,
        // Fewer than the hosted services get: a self hosted server may be a small one.
        FetchConcurrency: 4,
        // The probe asks for the builds queued since the newest one seen, so a quiet project need
        // not be fetched every five minutes.
        IdleCap: TimeSpan.FromMinutes(30));

    public static readonly ProviderDescriptor GitLab = new(
        Id: "gitlab",
        Name: "GitLab CI",
        BrowserSignIn: true,
        DeviceSignIn: true,
        SelfHosted: true,
        DefaultServer: "https://gitlab.com",
        TokenLabel: "Personal access token",
        TokenHelpUrl: "https://gitlab.com/-/user_settings/personal_access_tokens",
        Scheme: AuthScheme.HeaderPrivateToken,
        UserLabel: null,
        Scopes:
        [
            new("group", "Group", Required: false, Hint: "Leave empty for every project you are a member of")
        ],
        HasEstimate: false,
        HasBranches: true,
        HasPullRequests: true,
        CustomClientId: true,
        Notes: "The token needs the api scope to retry and cancel, or read_api to only watch.",
        // One GraphQL request covers fifty projects.
        FetchUnit: FetchUnit.Connection,
        ActionPermission: "the api scope");

    public static readonly ProviderDescriptor GoCd = new(
        Id: "gocd",
        Name: "GoCD",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: true,
        DefaultServer: null,
        TokenLabel: "Personal access token",
        TokenHelpUrl: "https://api.gocd.org/current/#access-tokens",
        Scheme: AuthScheme.Bearer,
        UserLabel: null,
        Scopes: [],
        HasEstimate: false,
        HasBranches: false,
        HasPullRequests: false,
        Notes: "Create the token at {server}/go/access_tokens.",
        // Fewer than the hosted services get: a self hosted server may be a small one.
        FetchConcurrency: 4,
        // The probe reads the dashboard, where a new instance moves the counter, so a quiet pipeline
        // need not be fetched every five minutes.
        IdleCap: TimeSpan.FromMinutes(30));

    public static readonly ProviderDescriptor Bitbucket = new(
        Id: "bitbucket",
        Name: "Bitbucket Pipelines",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: false,
        DefaultServer: "https://api.bitbucket.org/2.0",
        TokenLabel: "API token",
        TokenHelpUrl: "https://id.atlassian.com/manage-profile/security/api-tokens",
        Scheme: AuthScheme.BasicEmailToken,
        UserLabel: "Atlassian account email",
        Scopes:
        [
            new("workspace", "Workspace", Required: true)
        ],
        HasEstimate: false,
        HasBranches: true,
        HasPullRequests: true,
        Notes: "The token needs read:pipeline:bitbucket, write:pipeline:bitbucket, read:repository:bitbucket and read:workspace:bitbucket.",
        FetchConcurrency: Concurrently.Limit,
        // A thousand requests an hour, or the scaled limit the workspace reports.
        Quota: new(1000, TimeSpan.FromHours(1), 250, LearnLimit: true),
        IdleCap: TimeSpan.FromMinutes(30),
        ProbeInterval: TimeSpan.FromMinutes(1),
        ActionPermission: "write:pipeline:bitbucket");

    public static readonly ProviderDescriptor Octopus = new(
        Id: "octopus",
        Name: "Octopus Deploy",
        BrowserSignIn: false,
        DeviceSignIn: false,
        SelfHosted: true,
        DefaultServer: null,
        TokenLabel: "API key",
        TokenHelpUrl: "https://octopus.com/docs/api/authentication/create-an-api-key",
        Scheme: AuthScheme.HeaderOctopusApiKey,
        UserLabel: null,
        Scopes:
        [
            new("space", "Space", Required: false, Hint: "Leave empty for the default space")
        ],
        HasEstimate: true,
        HasBranches: false,
        HasPullRequests: false,
        FetchUnit: FetchUnit.Connection);

    public static readonly IReadOnlyList<ProviderDescriptor> All =
    [
        AppVeyor,
        AzureDevOps,
        Bitbucket,
        GitHub,
        GitLab,
        GoCd,
        Jenkins,
        Octopus,
        TeamCity,
        Travis
    ];

    public static ProviderDescriptor Get(string id) =>
        All.SingleOrDefault(_ => _.Id == id) ??
        throw new ArgumentException($"Unknown provider: {id}");

    public static ProviderDescriptor? ByName(string name) =>
        All.SingleOrDefault(_ => _.Name == name);
}
