# Authentication

Every provider takes a token or API key created in its own UI. Three also support signing in through the browser, which needs an OAuth application registered with the provider; the ids for the hosted services are compiled in once registered, and a self hosted server needs its own.

Credentials are kept in the platform's secret store: the Windows Data Protection API for the current user, the macOS login Keychain, or the Secret Service on Linux through `secret-tool`, falling back to a file only the user can read. settings.json never holds one.


## Tokens

| Provider | Credential | Where to create it | What it needs |
|---|---|---|---|
| AppVeyor | API token | https://ci.appveyor.com/api-keys | A v1 token works as is. A v2 (user level) token also needs the account name in the connection. |
| Azure DevOps | Personal access token | User settings, Personal access tokens (see [the docs](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate)) | Build: Read & execute |
| Bitbucket Pipelines | API token | https://id.atlassian.com/manage-profile/security/api-tokens | Scopes `read:pipeline:bitbucket`, `write:pipeline:bitbucket`, `read:repository:bitbucket`, `read:workspace:bitbucket`. Enter the Atlassian account email as the user. App passwords stopped working in June 2026. |
| GitHub Actions | Personal access token | Fine grained: https://github.com/settings/personal-access-tokens/new. Classic: https://github.com/settings/tokens | Fine grained: Actions read and write, Metadata read, on the repositories to watch. Classic: `repo`. |
| GitLab CI | Personal access token | https://gitlab.com/-/user_settings/personal_access_tokens | `api` to retry and cancel, `read_api` to only watch |
| GoCD | Personal access token | `https://<server>/go/access_tokens` | No scopes |
| Jenkins | API token | `https://<server>/me/security` | Enter the Jenkins user name as the user |
| Octopus Deploy | API key | Profile, My API Keys (see [the docs](https://octopus.com/docs/api/authentication/create-an-api-key)) | The user's permissions |
| TeamCity | Access token | Profile, Access Tokens (see [the docs](https://www.jetbrains.com/help/teamcity/configuring-your-user-profile.html#Managing+Access+Tokens)) | Same as the user, or limited to a project |
| Travis CI | API token | https://app.travis-ci.com/account/preferences | No scopes |

The connection editor links to the right page for the chosen provider.


## Browser and device sign in

| Provider | Browser | Device | Notes |
|---|---|---|---|
| GitHub Actions | Yes, with a registered OAuth App that has a client secret | Yes | The device flow needs only a client id and is the default. |
| GitLab CI | Yes, PKCE | Yes | Scopes `read_api` and `api`. A self hosted GitLab needs its own application; enter its id in the connection. |
| Azure DevOps | Yes, PKCE through Microsoft Entra ID | Yes | Work or school accounts only; Entra does not sign personal Microsoft accounts in to Azure DevOps. |

The browser flow opens the provider's sign in page in the default browser and listens on a loopback port for the redirect back. The device flow shows a code to enter on a page the browser opens, and needs no listener.

Refresh tokens are stored beside the access token and used when a poll is refused. If that fails too the connection shows "sign in required" and the tray icon turns amber until it is signed in again.


### Registering the applications

Until an application is registered its id in `OAuthClients.cs` is empty and the browser and device buttons report that. To register:

 * GitHub: an [OAuth App](https://github.com/settings/developers) with callback URL `http://127.0.0.1/callback` and "Enable Device Flow" ticked. GitHub matches the loopback callback on any port. The device flow needs only the client id; the browser flow also needs the client secret, embedded the way the GitHub CLI embeds its own.
 * GitLab: an [application](https://gitlab.com/-/user_settings/applications) with redirect URI `http://127.0.0.1/callback`, not confidential, scopes `read_api` and `api`.
 * Azure DevOps: a [Microsoft Entra app registration](https://portal.azure.com) for accounts in any organizational directory, with a mobile and desktop redirect URI of `http://localhost` and the delegated Azure DevOps `user_impersonation` permission.

A user can enter their own application id in the connection editor, which is how a self hosted GitLab or GitHub Enterprise Server signs in.
