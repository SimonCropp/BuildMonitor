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
        HasPullRequests: true);

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
        HasPullRequests: true);

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
        Notes: "Create the token at {server}/me/security.");

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
        Notes: "A fine grained token needs Actions read and write and Metadata read; a classic token needs the repo scope.");

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
        Notes: "The token needs Build (Read & execute).");

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
        Notes: "Create the token under Profile, Access Tokens.");

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
        Notes: "The token needs the api scope to retry and cancel, or read_api to only watch.");

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
        Notes: "Create the token at {server}/go/access_tokens.");

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
        Notes: "The token needs read:pipeline:bitbucket, write:pipeline:bitbucket, read:repository:bitbucket and read:workspace:bitbucket.");

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
        HasPullRequests: false);

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
