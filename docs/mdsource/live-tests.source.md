# Live provider tests

The provider tests in `src/BuildMonitor.Tests/Providers` run against canned JSON. They cannot catch a service that changed its API, a token scope the docs got wrong, or a redirect that drops a credential. The live tests run every provider against its real service instead:
 * the hosted services through sandbox accounts;
 * Jenkins, TeamCity and GoCD in Docker.

They live in `src/BuildMonitor.Tests/Providers/Live`. Both classes are `[Explicit]`, so a normal test run never includes them. The [Live workflow](/.github/workflows/live.yml) runs them on demand and weekly.


## What runs

`LiveReadTests` only reads. Each test runs once per provider.

| Test | Checks |
|---|---|
| `SignIn` | The connection test passes, and asking what the token may do answers. Where the connection test reports access (GitHub, GitLab, Octopus), both answers agree. |
| `Discovery` | At least one pipeline, with unique ids and absolute links. The sandbox pipeline is among them. |
| `Fetch` | Builds for the sandbox's poll group and up to five others. Every build belongs to a pipeline that was asked for, links to an absolute URL, and carries what a retry, cancel or log needs. No state the sandbox goes through maps to Unknown. |
| `ConditionalFetch` | A second fetch through the same cache revalidates every ETag the first received. When every answer is a 304, the builds are unchanged. |
| `RecentActivity` | The activity probe answers with tokens for real poll groups, and the tokens hold still while nothing changes. GitLab and Octopus have no probe. |
| `FailedBuildLog` | The newest failed build has a log, and a sandbox's log contains the marker `BuildMonitor live test`. |
| `PollCycles` | Two cycles of the app's own poller, two minutes apart on its clock, both end healthy. |

`LiveActionTests` changes builds, so it also needs `BUILDMONITOR_LIVE_ACTIONS=true`. It runs one round per provider against the sandbox pipeline only:

 1. Cancels anything an earlier round left running, then waits until the sandbox is idle.
 2. Retries the newest failed or cancelled build. Octopus has no retry, so it deploys the sandbox project's newest release instead.
 3. Once that build runs, cancels it. On Jenkins and TeamCity, the round first starts a second build, which waits in the queue behind the first, and cancels it there. On TeamCity it also moves that waiting build to the front of the queue first, which is the only moment a real build is sitting in a real queue. That proves the service accepts the call; whether the build moved would need a third run behind it, and the sandbox runs one at a time.
 4. Waits for the newest build to show Cancelled.
 5. Retries the cancelled build (Octopus deploys again), and waits for it to fail.
 6. Checks that the failed build offers a retry, and that its log contains the marker.

The round leaves the sandbox with a failed, retryable build, ready for the next round. A failure in step 5 usually means the app offers a retry the service refuses. That is a bug to fix in the provider, not in the test.


## Settings

The tests read each setting from the environment first, then from the file a local Docker server writes, then from user secrets. A blank value counts as unset.

`{ID}` is the provider id upper cased, with hyphens as underscores: `APPVEYOR`, `AZURE_DEVOPS`, `BITBUCKET`, `GITHUB`, `GITLAB`, `GOCD`, `JENKINS`, `OCTOPUS`, `TEAMCITY`, `TRAVIS`.

| Setting | Meaning |
|---|---|
| `BUILDMONITOR_{ID}_TOKEN` | The credential. Required. |
| `BUILDMONITOR_{ID}_SERVER` | The server, as in the connection editor. Required for GoCD, Jenkins, Octopus and TeamCity. |
| `BUILDMONITOR_{ID}_USER` | The user the token belongs to. Required for Bitbucket (the account email) and Jenkins. |
| `BUILDMONITOR_{ID}_SCOPE_{FIELD}` | A connection scope: `ACCOUNT` (AppVeyor), `ORGANIZATION` and `PROJECT` (Azure DevOps, organization required), `WORKSPACE` (Bitbucket, required), `OWNER` (GitHub), `GROUP` (GitLab), `SPACE` (Octopus), `PROJECT` (TeamCity). |
| `BUILDMONITOR_{ID}_PIPELINE` | The sandbox pipeline, by id or by name as the tray shows it. Required for the action round. |
| `BUILDMONITOR_{ID}_AUTH` | `Token` (the default), `Browser` or `Device`. Either sign in method sends the token the way a signed in connection does. |
| `BUILDMONITOR_{ID}_ACCESS` | What the token should be allowed to do: `Change`, `Watch` or `Unknown`. |
| `BUILDMONITOR_OCTOPUS_ENVIRONMENT` | The environment a sandbox deployment goes to. The space's first environment otherwise. |
| `BUILDMONITOR_LIVE_PROVIDERS` | `all` (the default), or a comma separated list of provider ids. A provider named here fails, rather than skips, when a setting it needs is missing. |
| `BUILDMONITOR_LIVE_ACTIONS` | `true` to run the action round. |
| `BUILDMONITOR_LIVE_QUEUE_MINUTES` | How long a started build may wait for a runner. 15 by default. |


## Running locally

Credentials go in user secrets, which live outside the repository:

```ps
dotnet user-secrets set BUILDMONITOR_GITHUB_TOKEN <token> --id BuildMonitor.LiveTests
dotnet user-secrets set BUILDMONITOR_GITHUB_SCOPE_OWNER <organization> --id BuildMonitor.LiveTests
dotnet user-secrets set BUILDMONITOR_GITHUB_PIPELINE Sandbox --id BuildMonitor.LiveTests
dotnet user-secrets list --id BuildMonitor.LiveTests
```

Then run the read tests, and the action round:

```ps
dotnet test --project src/BuildMonitor.Tests/BuildMonitor.Tests.csproj -- --treenode-filter "/*/*/LiveReadTests/*" --output Detailed
$env:BUILDMONITOR_LIVE_ACTIONS = 'true'
dotnet test --project src/BuildMonitor.Tests/BuildMonitor.Tests.csproj -- --treenode-filter "/*/*/LiveActionTests/*" --output Detailed
```

Set `BUILDMONITOR_LIVE_PROVIDERS` to run a single provider.


### Jenkins, TeamCity and GoCD in Docker

Each server has a compose file and a provisioning script in `src/BuildMonitor.Tests/Providers/Live/Servers`. Provisioning:
 * starts the server;
 * creates a user and a token;
 * creates the `buildmonitor-live` job, which prints the marker, waits a minute and fails;
 * waits for the job's first failed run.

It writes the settings to `Servers/.state/<provider>.env`, which the tests read. So no environment setup is needed.

```ps
& "$env:ProgramFiles\Git\bin\bash.exe" src/BuildMonitor.Tests/Providers/Live/Servers/jenkins/provision.sh
$env:BUILDMONITOR_LIVE_PROVIDERS = 'jenkins'
dotnet test --project src/BuildMonitor.Tests/BuildMonitor.Tests.csproj -- --treenode-filter "/*/*/LiveReadTests/*" --output Detailed
& "$env:ProgramFiles\Git\bin\bash.exe" src/BuildMonitor.Tests/Providers/Live/Servers/jenkins/provision.sh down
```

The scripts need bash, curl, openssl and Docker Compose 2.23 or later. Git Bash provides the first three on Windows. From PowerShell, name Git Bash explicitly, since a plain `bash` can start WSL instead.

The steps are `up`, `provision`, `logs` and `down`; `up provision` is the default. The ports are 8080 (Jenkins), 8111 (TeamCity) and 8153 (GoCD). `BUILDMONITOR_{ID}_PORT` moves one. Secrets and scripts reach the containers through Compose rather than file mounts, so the checkout does not need to be shared with Docker.

Starting the TeamCity server accepts the [TeamCity license agreement](https://www.jetbrains.com/legal/docs/teamcity/license/). TeamCity Professional is free for up to 100 build configurations and 3 agents, and this setup uses one of each. TeamCity's images download about 2.6 GB and take about 6.3 GB of disk once unpacked. Jenkins and GoCD take about 1 GB each.


## The Live workflow

| Job | Providers | Runs on | Retries and cancels |
|---|---|---|---|
| `hosted` | AppVeyor, Azure DevOps, Bitbucket, GitHub, GitLab, Octopus, Travis | Dispatch and the weekly schedule, on this repository only | Only when the `actions` input is ticked |
| `servers` | GoCD, Jenkins, TeamCity | Dispatch, the weekly schedule, and pull requests that change those providers | Always, since the servers are thrown away |

Each provider is its own job, so a failure names its provider.

The `hosted` job reads its credentials from the `live` environment. Create the environment before the workflow first runs, or the first run creates it without protection:
 * Create it at https://github.com/SimonCropp/BuildMonitor/settings/environments/new. Existing environments are listed at https://github.com/SimonCropp/BuildMonitor/settings/environments.
 * Under **Deployment branches and tags**, choose **Selected branches** and add `main`, so a workflow changed on another branch cannot read the secrets.
 * Add the secrets and variables below on the environment's own page, under **Environment secrets** and **Environment variables**.

| Environment secret | Value |
|---|---|
| `BUILDMONITOR_{ID}_TOKEN` | For each hosted provider with a sandbox |
| `BUILDMONITOR_OCTOPUS_SERVER` | The Octopus server, lower case, with no trailing slash |
| `BUILDMONITOR_BITBUCKET_USER` | The Atlassian account email |

| Environment variable | Value |
|---|---|
| `BUILDMONITOR_{ID}_PIPELINE` | For each hosted provider with a sandbox |
| `BUILDMONITOR_{ID}_SCOPE_{FIELD}` | As in the settings table |
| `BUILDMONITOR_OCTOPUS_ENVIRONMENT` | The sandbox environment |

`BUILDMONITOR_LIVE_HOSTED` lists the hosted providers that have a sandbox, as a JSON array such as `["github","azure-devops","octopus"]`. Runs for `all` cover only these, since a provider without a sandbox fails.

It is a repository variable, not an environment variable. Set it at https://github.com/SimonCropp/BuildMonitor/settings/variables/actions, under **Variables > New repository variable**. The workflow plans its jobs before a job starts, and an environment's variables are only read once it does.

The repository is public, and so are the workflow's logs. The runner masks secrets in logs, so the workflow:
 * lists every secret in the job;
 * uploads no artifacts, which are never masked;
 * turns off TUnit's report files and summary.

The tests print counts and statuses, and name what discovery found only when run locally. A failure can still print a request URL. So each token must reach its sandbox and nothing else.


## Sandboxes

Each hosted provider needs an account that holds one sandbox pipeline:
 * The pipeline prints `BuildMonitor live test`, waits a minute, and exits with a failure.
 * Run it once by hand before the first action round, so it has a failed build to retry.

Each token is created while signed in as the sandbox account:

| Provider | Create the token at | Notes |
|---|---|---|
| GitHub Actions | https://github.com/settings/personal-access-tokens/new | Resource owner: the sandbox organization. Repository access: the sandbox repository only. |
| Azure DevOps | `https://dev.azure.com/<organization>/_usersSettings/tokens` | Organization: the sandbox organization only. Scope: Build, Read & execute. |
| GitLab CI | https://gitlab.com/-/user_settings/personal_access_tokens | **Generate token > Fine-grained token**, limited to the sandbox group. |
| Bitbucket Pipelines | https://id.atlassian.com/manage-profile/security/api-tokens | **Create API token with scopes**, app Bitbucket. |
| AppVeyor | https://ci.appveyor.com/api-keys | The v1 key. |
| Travis CI | https://app.travis-ci.com/account/preferences | Signed in as the sandbox GitHub user. |
| Octopus Deploy | **Configuration > Users** on the instance, then the service account | **New API Key**. The instance's own address, so there is no fixed link. |


### GitHub Actions

 * Create an organization for the sandbox, with a public repository `sandbox`. Public repositories get GitHub hosted runners for free.
 * Create a fine grained personal access token at https://github.com/settings/personal-access-tokens/new. Set its resource owner to the organization, and give it access to the `sandbox` repository only, with Actions read and write and Metadata read.
   * Its connection test reports access as Unknown, since GitHub does not say what a fine grained token may do.
 * Settings: `BUILDMONITOR_GITHUB_SCOPE_OWNER` is the organization, and `BUILDMONITOR_GITHUB_PIPELINE` is `Sandbox`.

`.github/workflows/sandbox.yml`:

```yml
name: Sandbox
on:
  workflow_dispatch:
permissions: {}
jobs:
  fail:
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - name: Fail after a minute
        run: |
          echo "BuildMonitor live test"
          sleep 60
          exit 1
```

GitHub refuses to re-run a run more than 30 days old, and BuildMonitor leaves out repositories not pushed to in 90 days. So the sandbox also needs a keepalive, `.github/workflows/keepalive.yml`:

```yml
name: Keepalive
on:
  schedule:
    - cron: '41 3 1,15 * *'
  workflow_dispatch:
permissions:
  contents: write
  actions: write
jobs:
  keepalive:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - name: Push, so discovery keeps the repository
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git commit --allow-empty --message keepalive
          git push --force origin HEAD:refs/heads/keepalive
      - name: Start a fresh failing run
        env:
          GH_TOKEN: ${{ github.token }}
        run: gh workflow run sandbox.yml --repo "$GITHUB_REPOSITORY" --ref main
```


### Azure DevOps

 * Create an organization for the sandbox, with a private project `Sandbox` and an Azure Repos repository.
 * Microsoft-hosted agents are free once an Azure subscription is linked under Organization settings, Billing. A self hosted agent is free without one.
 * Add `azure-pipelines.yml` as a new pipeline and run it once.
 * Retention deletes failed runs, so open the failed run's menu and choose **Retain**.
 * Create a personal access token at `https://dev.azure.com/<organization>/_usersSettings/tokens`, limited to the sandbox organization, with the custom scope Build: Read & execute. If `SignIn` is refused, add Project and Team: Read.
 * Settings: `BUILDMONITOR_AZURE_DEVOPS_SCOPE_ORGANIZATION`, `BUILDMONITOR_AZURE_DEVOPS_SCOPE_PROJECT` is `Sandbox`, and `BUILDMONITOR_AZURE_DEVOPS_PIPELINE` is the pipeline's name.

```yml
trigger: none
pr: none
pool:
  vmImage: ubuntu-latest
jobs:
  - job: fail
    displayName: Fail
    timeoutInMinutes: 10
    steps:
      - checkout: none
      - bash: |
          echo "BuildMonitor live test"
          sleep 60
          exit 1
        displayName: Fail after a minute
```


### GitLab CI

 * Create a group `buildmonitor-sandbox` with a project `sandbox`.
 * GitLab hosted runners need the account's identity verified, and the Free tier includes 400 compute minutes a month.
 * Create a fine grained personal access token at https://gitlab.com/-/user_settings/personal_access_tokens (**Generate token > Fine-grained token**), limited to the group, with these permissions: User read, Personal access token read, Project read, Pipeline read and update, Job read.
 * GitLab archives pipelines after a year, so start a new one whenever the token is renewed.
 * Settings: `BUILDMONITOR_GITLAB_SCOPE_GROUP` is `buildmonitor-sandbox`, and `BUILDMONITOR_GITLAB_PIPELINE` is `buildmonitor-sandbox/sandbox`.

`.gitlab-ci.yml`, which runs only when started from the web or the API:

```yml
workflow:
  rules:
    - if: $CI_PIPELINE_SOURCE == "web" || $CI_PIPELINE_SOURCE == "api"
variables:
  GIT_STRATEGY: none
fail:
  image: alpine:3
  timeout: 10m
  script:
    - echo "BuildMonitor live test"
    - sleep 60
    - exit 1
```


### Bitbucket Pipelines

 * Create a workspace with a repository `sandbox`, and enable Pipelines in its settings.
 * The Free plan includes 50 build minutes a month, which is about a dozen action rounds.
 * Create an API token at https://id.atlassian.com/manage-profile/security/api-tokens (**Create API token with scopes**, app Bitbucket), with the scopes `read:workspace:bitbucket`, `read:repository:bitbucket`, `read:pipeline:bitbucket` and `write:pipeline:bitbucket`. A token reaches every workspace its account can, so use an account that belongs to the sandbox workspace only.
 * Retry starts the branch's default pipeline, so the script must be the `default` pipeline.
 * Settings: `BUILDMONITOR_BITBUCKET_USER` is the account email, `BUILDMONITOR_BITBUCKET_SCOPE_WORKSPACE` is the workspace, and `BUILDMONITOR_BITBUCKET_PIPELINE` is `workspace/sandbox`.

`bitbucket-pipelines.yml`:

```yml
image: alpine:3
clone:
  enabled: false
pipelines:
  default:
    - step:
        name: Fail
        max-time: 5
        script:
          - echo "BuildMonitor live test"
          - sleep 60
          - exit 1
```


### AppVeyor

 * Create a separate AppVeyor account, and add a project from a public repository `sandbox-appveyor`. The free plan builds public repositories, one job at a time.
 * Use the account's v1 API key, from https://ci.appveyor.com/api-keys, and leave `BUILDMONITOR_APPVEYOR_SCOPE_ACCOUNT` unset. A v2 key reaches every account its user belongs to.
 * Settings: `BUILDMONITOR_APPVEYOR_PIPELINE` is the project's name.

`appveyor.yml`:

```yml
version: '{build}'
image: Ubuntu
skip_tags: true
clone_depth: 1
build_script:
  - sh: bash -c 'echo "BuildMonitor live test"; sleep 60; exit 1'
test: off
```


### Travis CI

 * Travis has no free plan for new accounts. The usage based plan works, and so do open source credits, which Travis support grants on request.
 * Sign in to Travis with a separate GitHub user, whose only access is to a repository `sandbox-travis`. A Travis token acts as its user on everything that user can reach.
 * Copy that user's API token from https://app.travis-ci.com/account/preferences.
 * Settings: `BUILDMONITOR_TRAVIS_PIPELINE` is the repository's slug, `owner/sandbox-travis`.

`.travis.yml`:

```yml
os: linux
dist: jammy
language: shell
git:
  depth: 1
script:
  - sh -c 'echo "BuildMonitor live test"; sleep 60; exit 1'
```


### Octopus Deploy

 * Use a space kept for the sandbox, on an existing instance or a separate Octopus Cloud Free instance.
   * Cloud Free has one space, and deactivates an instance that deploys nothing for 60 days.
 * Create an environment `Sandbox`, and a project `BuildMonitor Sandbox`.
 * Add one **Run a Script** step that runs once on a worker from the dynamic Ubuntu pool:

   ```bash
   echo "BuildMonitor live test"
   sleep 60
   exit 1
   ```

 * Leave guided failure off. Otherwise a failed deployment waits for a person.
 * Create release 0.0.1 and deploy it to `Sandbox` once.
 * Create a service account in a team scoped to the sandbox, with Project viewer, Deployment creator and TaskCancel. Create its API key from the service account's page under **Configuration > Users**.
   * Its API key expires after 180 days by default.
 * Settings: `BUILDMONITOR_OCTOPUS_SERVER`, `BUILDMONITOR_OCTOPUS_SCOPE_SPACE`, `BUILDMONITOR_OCTOPUS_PIPELINE` is `BuildMonitor Sandbox`, and `BUILDMONITOR_OCTOPUS_ENVIRONMENT` is `Sandbox`.


## Token renewal

A token that expires fails the weekly run's `SignIn` test for its provider.

| Provider | Longest lifetime | After renewing |
|---|---|---|
| GitHub Actions | As chosen, up to no expiry | Update the secret |
| Azure DevOps | 1 year | Update the secret |
| GitLab CI | 365 days | Update the secret, and start a new sandbox pipeline |
| Bitbucket Pipelines | 1 year | Update the secret |
| AppVeyor | No expiry | Nothing |
| Travis CI | No expiry | Nothing |
| Octopus Deploy | 180 days by default | Update the secret |
| Jenkins, TeamCity, GoCD | One run | Nothing: provisioning creates a new token each time |
