/// <summary>
/// The application registrations the browser and device flows sign in through. The ids are
/// filled in once the applications are registered; until then, or for a self hosted server, a
/// user supplies their own in the connection editor.
/// <para>
/// GitHub: an OAuth App with "Enable Device Flow" ticked and callback http://127.0.0.1/callback,
/// which GitHub matches on any port. GitLab: an application with redirect
/// http://127.0.0.1/callback, not confidential, scopes read_api and api. Azure DevOps: a
/// multi-tenant Microsoft Entra registration with a mobile and desktop redirect of
/// http://localhost and the Azure DevOps user_impersonation permission.
/// </para>
/// </summary>
static class OAuthClients
{
    public const string GitHubClientId = "Ov23lidsjnhnW0pniEFX";
    // Bundled on purpose. GitHub OAuth Apps have no PKCE, so every desktop app offering the web
    // flow ships its secret, as the GitHub CLI does. It grants nothing by itself; a leak means
    // regenerating it on github.com and shipping a new version.
    public const string GitHubClientSecret = "ed9358ff15a34512bd504c0055cd24f8e5b35783";
    public const string GitLabClientId = "";
    public const string EntraClientId = "";

    const string azureDevOpsResource = "499b84ac-1321-427f-aa17-267ca6975798";

    public static OAuthClient? For(Connection connection)
    {
        var provider = ProviderDescriptors.Get(connection.ProviderId);
        var server = (connection.Server ?? provider.DefaultServer ?? "").TrimEnd('/');
        switch (connection.ProviderId)
        {
            case "github":
            {
                var hosted = server.Length == 0 || server == "https://api.github.com";
                var web = hosted ? "https://github.com" : server;
                var clientId = connection.ClientId ?? (hosted ? GitHubClientId : "");
                if (clientId.Length == 0)
                {
                    return null;
                }

                return new(
                    "github",
                    $"{web}/login/oauth/authorize",
                    $"{web}/login/oauth/access_token",
                    $"{web}/login/device/code",
                    clientId,
                    hosted && connection.ClientId is null && GitHubClientSecret.Length > 0 ? GitHubClientSecret : null,
                    ["repo"],
                    "http://127.0.0.1:{port}/callback",
                    Pkce: true);
            }
            case "gitlab":
            {
                var hosted = server.Length == 0 || server == "https://gitlab.com";
                var web = hosted ? "https://gitlab.com" : server;
                var clientId = connection.ClientId ?? (hosted ? GitLabClientId : "");
                if (clientId.Length == 0)
                {
                    return null;
                }

                return new(
                    "gitlab",
                    $"{web}/oauth/authorize",
                    $"{web}/oauth/token",
                    $"{web}/oauth/authorize_device",
                    clientId,
                    null,
                    ["read_api", "api"],
                    "http://127.0.0.1:{port}/callback",
                    Pkce: true);
            }
            case "azure-devops":
            {
                var clientId = connection.ClientId ?? EntraClientId;
                if (clientId.Length == 0)
                {
                    return null;
                }

                return new(
                    "azure-devops",
                    "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize",
                    "https://login.microsoftonline.com/organizations/oauth2/v2.0/token",
                    "https://login.microsoftonline.com/organizations/oauth2/v2.0/devicecode",
                    clientId,
                    null,
                    [$"{azureDevOpsResource}/.default", "offline_access"],
                    "http://localhost:{port}",
                    Pkce: true);
            }
            default:
                return null;
        }
    }
}
