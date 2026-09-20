#Requires -Version 7.4
<#
.SYNOPSIS
Sets up the hosted sandboxes the live tests run against, and the GitHub environment the Live
workflow reads their settings from. See docs/live-tests.md.

.DESCRIPTION
Does everything an API allows, and stops only for what a person has to do: signing up, creating
an organization or a workspace, connecting a service to GitHub, and creating tokens. Each of those
steps says what to do, opens the page, and waits.

Tokens are read without being shown and go straight to the environment's secrets. They are
written nowhere else, and never into this file. Where a service needs more access to set up than
the tests need, the script asks for a temporary token first, and revokes it where the service
allows. Otherwise it reminds you to.

Every step checks before it creates, so running the script again is safe. A provider that
already has a token is skipped unless you choose to set it up again, which is also how a token is
renewed.

.PARAMETER Provider
The providers to set up. All the hosted ones by default.

.PARAMETER Local
Also writes each provider's settings to the user secrets the live tests read when run locally.

.EXAMPLE
./provision.ps1

.EXAMPLE
./provision.ps1 -Provider github, octopus -Local
#>
[CmdletBinding()]
param(
    [string[]] $Provider = @('github', 'azure-devops', 'gitlab', 'bitbucket', 'appveyor', 'travis', 'octopus'),
    [switch] $Local,
    [string] $Repository = 'SimonCropp/BuildMonitor',
    [string] $Environment = 'live'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

# Split here as well as by the parameter binder, since a list given to a script run as
# "pwsh provision.ps1 -Provider one,two" arrives as one string.
$known = @('github', 'azure-devops', 'gitlab', 'bitbucket', 'appveyor', 'travis', 'octopus')
$Provider = @($Provider -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
$unknown = @($Provider | Where-Object { $known -notcontains $_ })
if ($unknown.Count -gt 0) {
    throw "Unknown provider: $($unknown -join ', '). Choose from: $($known -join ', ')."
}

# The sandbox pipelines, as docs/live-tests.md gives them. Each prints the marker the tests look
# for, waits a minute, and fails.

$githubSandbox = @'
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
'@

$githubKeepalive = @'
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
'@

$azurePipeline = @'
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
'@

$gitlabCi = @'
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
'@

$bitbucketPipelines = @'
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
'@

$appveyorYml = @'
version: '{build}'
image: Ubuntu
skip_tags: true
clone_depth: 1
build_script:
  - sh: bash -c 'echo "BuildMonitor live test"; sleep 60; exit 1'
test: off
'@

$travisYml = @'
os: linux
dist: jammy
language: shell
git:
  depth: 1
script:
  - sh -c 'echo "BuildMonitor live test"; sleep 60; exit 1'
'@

$octopusScript = @'
echo "BuildMonitor live test"
sleep 60
exit 1
'@

# Output

function Write-Section([string] $text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Write-Done([string] $text) {
    Write-Host "   $text" -ForegroundColor Green
}

function Write-Note([string] $text) {
    Write-Host "   $text" -ForegroundColor DarkYellow
}

# Input

# A step only a person can do: says what, opens the pages, and waits. -Show only lists the pages,
# for a step done as another user than the default browser is signed in as.
function Request-Person([string[]] $lines, [string[]] $urls = @(), [switch] $Show) {
    Write-Host ''
    Write-Host '>> Your turn' -ForegroundColor Yellow
    foreach ($line in $lines) {
        Write-Host "   $line" -ForegroundColor Yellow
    }

    foreach ($url in $urls) {
        Write-Host "   $url" -ForegroundColor Yellow
        if (-not $Show) {
            Start-Process $url
        }
    }

    $null = Read-Host '   Press Enter when done'
}

# A value the script already knows is offered rather than asked for, and the prompt says so: the
# bare "[value]" convention reads as a label, and a stored organization was typed back in by hand
# twice before this said what the empty answer does.
function Read-Value([string] $prompt, [string] $default) {
    $suffix = ''
    if ($default) {
        $suffix = " [Enter to keep $default]"
    }

    $value = Read-Host ">> $prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = $default
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$prompt is required"
    }

    return $value.Trim()
}

function Read-Token([string] $what) {
    $secure = Read-Host ">> Paste $what (it is not shown)" -AsSecureString
    $plain = [System.Net.NetworkCredential]::new('', $secure).Password.Trim()
    if ($plain.Length -eq 0) {
        throw "No $what was entered"
    }

    return $plain
}

function Read-YesNo([string] $question) {
    return (Read-Host ">> $question [y/N]") -match '^\s*y(es)?\s*$'
}

# GitHub, through gh

function Invoke-Gh {
    $output = & gh @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($args -join ' ') failed: $output"
    }

    return $output
}

# Null when the path does not exist or cannot be read.
function Get-GhJson([string] $path) {
    $json = & gh api $path 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return $json | ConvertFrom-Json
}

function Send-GhJson([string] $method, [string] $path, $body) {
    $output = ConvertTo-Json -InputObject $body -Depth 10 | & gh api --method $method $path --input - 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh api $method $path failed: $output"
    }

    return $output | ConvertFrom-Json
}

function Get-Setting([string] $name, [string] $default) {
    $value = & gh variable get $name --repo $script:Repository --env $script:Environment 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace("$value")) {
        return "$value".Trim()
    }

    return $default
}

function Get-SecretNames {
    $json = & gh secret list --repo $script:Repository --env $script:Environment --json name 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    return @($json | ConvertFrom-Json | ForEach-Object name)
}

function Save-Settings([System.Collections.IDictionary] $secrets, [System.Collections.IDictionary] $variables) {
    foreach ($name in $secrets.Keys) {
        # Piped rather than passed with --body, so the value is never on a command line.
        $output = $secrets[$name] | & gh secret set $name --repo $script:Repository --env $script:Environment 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Setting the secret $name failed: $output"
        }

        Write-Done "secret $name"
    }

    foreach ($name in $variables.Keys) {
        $output = & gh variable set $name --repo $script:Repository --env $script:Environment --body $variables[$name] 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Setting the variable $name failed: $output"
        }

        Write-Done "variable $name = $($variables[$name])"
    }

    if (-not $script:Local) {
        return
    }

    $all = [ordered]@{}
    foreach ($name in $secrets.Keys) {
        $all[$name] = $secrets[$name]
    }

    foreach ($name in $variables.Keys) {
        $all[$name] = $variables[$name]
    }

    $output = ConvertTo-Json -InputObject $all | & dotnet user-secrets set --id BuildMonitor.LiveTests 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Writing the user secrets failed: $output"
    }

    Write-Done 'user secrets, for local runs'
}

# Other services, over HTTP

function Invoke-Api {
    param(
        [string] $Uri,
        [string] $Method = 'GET',
        [hashtable] $Headers = @{},
        $Body,
        [hashtable] $Form,
        [int[]] $Allow = @()
    )

    # A URL built around a value that was not there. Empty it is, or relative where the part before
    # the path was, and Invoke-RestMethod calls both a hostname it could not parse, which reads as a
    # bad server name rather than as a missing value and names neither the call that wanted it nor
    # the one that came back short.
    if ($Uri -notmatch '^https?://') {
        $caller = @(Get-PSCallStack)[1]
        throw "Not a URL to call: '$Uri', wanted at line $($caller.ScriptLineNumber) of $($caller.FunctionName). Something the service was expected to return did not come back."
    }

    $parameters = @{
        Uri = $Uri
        Method = $Method
        Headers = $Headers
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'status'
    }
    if ($null -ne $Body) {
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 20
        $parameters.ContentType = 'application/json'
    }

    if ($Form) {
        $parameters.Form = $Form
    }

    $response = Invoke-RestMethod @parameters
    if ($status -ge 200 -and $status -lt 300) {
        # Azure DevOps answers a refused token with 203 and a sign-in page rather than 401, and a
        # client that follows redirects sees the same for its 302. Taken for success, the HTML flows
        # on as an object with no properties, and the run stops several calls later on a URL built
        # from one of them, saying only that a hostname could not be parsed.
        if ($response -is [string] -and $response -match '(?i)<html') {
            throw "$Method $Uri answered $status with a sign-in page rather than JSON, which means the token was refused. Check it belongs to this organization, has not expired, and has the scopes named above."
        }

        return $response
    }

    if ($Allow -contains $status) {
        return $null
    }

    $detail = $response
    if ($response -isnot [string]) {
        $detail = ConvertTo-Json -InputObject $response -Depth 5 -Compress
    }

    throw "$Method $Uri answered $status. $detail"
}

function Get-Basic([string] $user, [string] $password) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("${user}:${password}")
    return "Basic $([Convert]::ToBase64String($bytes))"
}

# Polls until the condition holds. False once it gives up, so a runner that is slow to start warns
# rather than fails: the next run of the script picks up from there.
function Wait-For([string] $what, [scriptblock] $condition, [int] $minutes = 15) {
    $deadline = [DateTime]::UtcNow.AddMinutes($minutes)
    Write-Host "   waiting for $what" -NoNewline
    while ($true) {
        if (& $condition) {
            Write-Host ' done' -ForegroundColor Green
            return $true
        }

        if ([DateTime]::UtcNow -gt $deadline) {
            Write-Host ''
            Write-Note "Gave up after $minutes minutes. Run the script again once it has happened."
            return $false
        }

        Write-Host '.' -NoNewline
        Start-Sleep -Seconds 10
    }
}

# Files written through an API get the same bytes whatever line endings this script was checked
# out with.
function Format-File([string] $text) {
    return ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
}

# The GitHub environment

function Initialize-Environment {
    Write-Section "GitHub environment '$script:Environment' on $script:Repository"
    $current = Get-GhJson "repos/$script:Repository/environments/$script:Environment"
    if (-not $current -or -not $current.deployment_branch_policy -or -not $current.deployment_branch_policy.custom_branch_policies) {
        $null = Send-GhJson PUT "repos/$script:Repository/environments/$script:Environment" @{
            deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
        }
        Write-Done 'limited to selected branches'
    }

    $policies = Get-GhJson "repos/$script:Repository/environments/$script:Environment/deployment-branch-policies"
    if (-not ($policies.branch_policies | Where-Object name -eq 'main')) {
        $null = Send-GhJson POST "repos/$script:Repository/environments/$script:Environment/deployment-branch-policies" @{ name = 'main'; type = 'branch' }
        Write-Done 'main added as the one branch that may read it'
    }
}

# GitHub Actions

function Initialize-GhRepo([string] $slug, [System.Collections.IDictionary] $files) {
    if (-not (Get-GhJson "repos/$slug")) {
        $null = Invoke-Gh repo create $slug --public --add-readme
        Write-Done "created $slug"
    }

    foreach ($path in $files.Keys) {
        $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Format-File $files[$path])))
        $existing = Get-GhJson "repos/$slug/contents/$path"
        if ($existing -and ($existing.content -replace '\s', '') -eq $encoded) {
            continue
        }

        $body = @{ message = "Add $path"; content = $encoded }
        if ($existing) {
            $body.message = "Update $path"
            $body.sha = $existing.sha
        }

        $null = Send-GhJson PUT "repos/$slug/contents/$path" $body
        Write-Done "wrote $path to $slug"
    }
}

# A failed run that reached a job, which is the only kind that leaves a log behind. A run that broke
# before starting one, as a workflow with bad YAML does, is still reported as a failed run; counting
# it would call the sandbox ready and leave the live tests reading a log that was never written.
# Asked the way the provider asks it: jobs of the latest attempt whose conclusion is failure.
function Test-GhFailure([string] $slug, [string] $workflow) {
    $runs = Get-GhJson "repos/$slug/actions/workflows/$workflow/runs?status=failure&per_page=5"
    if (-not $runs -or $runs.total_count -eq 0) {
        return $false
    }

    foreach ($run in $runs.workflow_runs) {
        $jobs = Get-GhJson "repos/$slug/actions/runs/$($run.id)/jobs?filter=latest&per_page=100"
        if ($jobs -and @($jobs.jobs | Where-Object { $_.conclusion -in 'failure', 'timed_out' }).Count -gt 0) {
            return $true
        }
    }

    return $false
}

function Start-GhFailure([string] $slug, [string] $workflow) {
    if (Test-GhFailure $slug $workflow) {
        return
    }

    $runs = Get-GhJson "repos/$slug/actions/workflows/$workflow/runs?per_page=1"
    if (-not $runs -or $runs.total_count -eq 0) {
        # A workflow pushed a moment ago takes a moment to be known.
        $null = Wait-For "GitHub to pick up $workflow" { $null -ne (Get-GhJson "repos/$slug/actions/workflows/$workflow") } 2
        $null = Invoke-Gh workflow run $workflow --repo $slug --ref main
        Write-Done "started $workflow"
    }

    $null = Wait-For "a failed $workflow run" { Test-GhFailure $slug $workflow }
}

function Initialize-GitHub {
    $owner = Read-Value 'GitHub sandbox organization' (Get-Setting BUILDMONITOR_GITHUB_SCOPE_OWNER)
    if (-not (Get-GhJson "orgs/$owner")) {
        Request-Person @("Create the organization '$owner', on the Free plan.") 'https://github.com/account/organizations/new?plan=free'
        if (-not (Get-GhJson "orgs/$owner")) {
            throw "The organization $owner was not found"
        }
    }

    Initialize-GhRepo "$owner/sandbox" ([ordered]@{
        '.github/workflows/sandbox.yml' = $githubSandbox
        '.github/workflows/keepalive.yml' = $githubKeepalive
    })
    Start-GhFailure "$owner/sandbox" 'sandbox.yml'

    $query = "name=BuildMonitor+live+tests&description=Reads%2C+retries+and+cancels+the+sandbox&target_name=$owner&expires_in=366&actions=write"
    Request-Person @(
        "Create the token the live tests use. The page is filled in apart from the repository:",
        "  Repository access: Only select repositories, then $owner/sandbox.",
        'Check Actions says Read and write, then Generate token and copy it.'
    ) "https://github.com/settings/personal-access-tokens/new?$query"
    $token = Read-Token 'the GitHub token'
    $null = Invoke-Api "https://api.github.com/repos/$owner/sandbox/actions/workflows" -Headers @{ Authorization = "Bearer $token" }
    Write-Done 'the token reads the sandbox'

    Save-Settings ([ordered]@{ BUILDMONITOR_GITHUB_TOKEN = $token }) ([ordered]@{
        BUILDMONITOR_GITHUB_SCOPE_OWNER = $owner
        BUILDMONITOR_GITHUB_PIPELINE = 'Sandbox'
    })
}

# Azure DevOps

function Initialize-AzureDevOps {
    $organization = Read-Value 'Azure DevOps sandbox organization, as in dev.azure.com/<organization>' (Get-Setting BUILDMONITOR_AZURE_DEVOPS_SCOPE_ORGANIZATION)
    $base = "https://dev.azure.com/$organization"
    Request-Person @(
        "Create the organization '$organization' if it does not exist (first page, New organization).",
        'Then create a temporary token for this script (second page, New Token):',
        "  Organization: $organization. Expiration: tomorrow. Scopes: Full access."
    ) @('https://aex.dev.azure.com/me', "$base/_usersSettings/tokens")
    $setupToken = Read-Token 'the temporary Azure DevOps token'
    $headers = @{ Authorization = Get-Basic '' $setupToken }
    try {
        # A token made for another organization still authenticates here: Azure DevOps answers with
        # an empty list rather than refusing it. Left to run on, the script reads that as an
        # organization with nothing in it and sets about creating a project it has no right to.
        if (@((Invoke-Api "$base/_apis/projects?api-version=7.1" -Headers $headers).value).Count -eq 0) {
            throw "The token sees no projects in $organization, so it was made for another organization. Create it from $base/_usersSettings/tokens, which has this one already chosen."
        }

        $project = Invoke-Api "$base/_apis/projects/Sandbox?api-version=7.1" -Headers $headers -Allow 404
        if (-not $project) {
            $process = @((Invoke-Api "$base/_apis/process/processes?api-version=7.1" -Headers $headers).value) | Where-Object isDefault | Select-Object -First 1
            $operation = Invoke-Api "$base/_apis/projects?api-version=7.1" -Method POST -Headers $headers -Body @{
                name = 'Sandbox'
                visibility = 'private'
                capabilities = @{
                    versioncontrol = @{ sourceControlType = 'Git' }
                    processTemplate = @{ templateTypeId = $process.id }
                }
            }
            $null = Wait-For 'the Sandbox project' {
                $state = (Invoke-Api $operation.url -Headers $headers).status
                if ($state -eq 'failed' -or $state -eq 'cancelled') {
                    throw "Creating the Sandbox project $state"
                }

                $state -eq 'succeeded'
            } 5
            $project = Invoke-Api "$base/_apis/projects/Sandbox?api-version=7.1" -Headers $headers
            Write-Done 'created the project Sandbox'
        }

        $gitRepository = Invoke-Api "$base/Sandbox/_apis/git/repositories/Sandbox?api-version=7.1" -Headers $headers
        # An empty repository answers 400 rather than 404.
        $item = Invoke-Api "$base/Sandbox/_apis/git/repositories/$($gitRepository.id)/items?path=/azure-pipelines.yml&api-version=7.1" -Headers $headers -Allow 400, 404
        if (-not $item) {
            $heads = @((Invoke-Api "$base/Sandbox/_apis/git/repositories/$($gitRepository.id)/refs?filter=heads/main&api-version=7.1" -Headers $headers).value)
            $old = '0000000000000000000000000000000000000000'
            if ($heads.Count -gt 0) {
                $old = $heads[0].objectId
            }

            $null = Invoke-Api "$base/Sandbox/_apis/git/repositories/$($gitRepository.id)/pushes?api-version=7.1" -Method POST -Headers $headers -Body @{
                refUpdates = @(@{ name = 'refs/heads/main'; oldObjectId = $old })
                commits = @(@{
                    comment = 'Add azure-pipelines.yml'
                    changes = @(@{
                        changeType = 'add'
                        item = @{ path = '/azure-pipelines.yml' }
                        newContent = @{ content = (Format-File $azurePipeline); contentType = 'rawtext' }
                    })
                })
            }
            Write-Done 'pushed azure-pipelines.yml'
        }

        $pipelines = "$base/Sandbox/_apis/pipelines?api-version=7.1"
        $pipeline = @((Invoke-Api $pipelines -Headers $headers).value) | Where-Object name -eq 'Sandbox' | Select-Object -First 1
        if (-not $pipeline) {
            $pipeline = Invoke-Api $pipelines -Method POST -Headers $headers -Body @{
                name = 'Sandbox'
                folder = '\'
                configuration = @{
                    type = 'yaml'
                    path = '/azure-pipelines.yml'
                    repository = @{ id = $gitRepository.id; name = $gitRepository.name; type = 'azureReposGit' }
                }
            }
            Write-Done 'created the pipeline Sandbox'
        }

        $runsPath = "$base/Sandbox/_apis/pipelines/$($pipeline.id)/runs"
        # Braced, because a question mark is a valid character in a variable name: unbraced, the
        # parser reads "$runsPath?api" as the name, finds nothing, and leaves the query string
        # standing on its own as the URL.
        $runs = "${runsPath}?api-version=7.1"
        if (-not (Get-AzureFailure $base $runs $headers)) {
            Request-Person @(
                'Microsoft-hosted jobs need one of these. Skip this if either is done:',
                '  link an Azure subscription (this page), or',
                '  ask for the free grant for private projects, which takes a few days: https://aka.ms/azpipelines-parallelism-request'
            ) "$base/_settings/billing"
            $started = Invoke-Api $runs -Method POST -Headers $headers -Body @{}
            Write-Done 'started the pipeline'
            $null = Wait-For 'the run to finish' {
                (Invoke-Api "$runsPath/$($started.id)?api-version=7.1" -Headers $headers).state -eq 'completed'
            }

            # A run refused an agent fails in under a second with nothing in its timeline, and every
            # API that reports a failed run reports it as one. Without this the script would call the
            # sandbox ready, and the failure would surface days later as a live test reading a log
            # that was never written.
            if (-not (Test-AzureLogged $base $started.id $headers)) {
                throw "Run $($started.id) failed before it started a job, so it has no log for the live tests to read. Microsoft-hosted parallelism is the usual reason: $base/_settings/buildqueue?_a=concurrentJobs"
            }
        }

        # Retention deletes failed runs, and the action round needs one to retry.
        $failed = Get-AzureFailure $base $runs $headers
        if ($failed) {
            $leases = "$base/Sandbox/_apis/build/builds/$($failed.id)/leases?api-version=7.1"
            if (@((Invoke-Api $leases -Headers $headers).value).Count -eq 0) {
                $user = (Invoke-Api "$base/_apis/connectionData" -Headers $headers).authenticatedUser.id
                $null = Invoke-Api "$base/Sandbox/_apis/build/retention/leases?api-version=7.1" -Method POST -Headers $headers -Body @(@{
                    daysValid = 36500
                    definitionId = $pipeline.id
                    ownerId = "User:$user"
                    protectPipeline = $false
                    runId = $failed.id
                })
                Write-Done "retained run $($failed.id)"
            }
        }

        Request-Person @(
            'Create the token the live tests use (New Token):',
            "  Name: BuildMonitor live tests. Organization: $organization. Expiration: up to a year.",
            '  Scopes: Custom defined, then Build: Read & execute.'
        ) "$base/_usersSettings/tokens"
        $token = Read-Token 'the Azure DevOps token'
        $null = Invoke-Api "$base/Sandbox/_apis/build/builds?definitions=$($pipeline.id)&`$top=1&api-version=7.1" -Headers @{ Authorization = Get-Basic '' $token }
        Write-Done 'the token reads the sandbox'

        Save-Settings ([ordered]@{ BUILDMONITOR_AZURE_DEVOPS_TOKEN = $token }) ([ordered]@{
            BUILDMONITOR_AZURE_DEVOPS_SCOPE_ORGANIZATION = $organization
            BUILDMONITOR_AZURE_DEVOPS_SCOPE_PROJECT = 'Sandbox'
            BUILDMONITOR_AZURE_DEVOPS_PIPELINE = 'Sandbox'
        })
    }
    finally {
        # Azure DevOps only lets a Microsoft Entra sign-in revoke a token.
        Write-Note "Revoke the temporary token (the one with Full access) at $base/_usersSettings/tokens"
    }
}

# The newest failed run the live tests can actually use. Bounded, because the list grows by a run
# every time the sandbox is rebuilt and only the newest few are ever candidates.
function Get-AzureFailure([string] $base, [string] $runs, [hashtable] $headers) {
    $failures = @((Invoke-Api $runs -Headers $headers).value) | Where-Object result -eq 'failed' | Select-Object -First 5
    foreach ($run in $failures) {
        if (Test-AzureLogged $base $run.id $headers) {
            return $run
        }
    }

    return $null
}

# Whether a failed run reached a job, which is the only kind that leaves a log behind. Asked the way
# the provider asks it: a failed timeline record carrying a log is exactly what FetchLog reads, so
# the script and the test cannot disagree about whether the sandbox is ready.
function Test-AzureLogged([string] $base, $runId, [hashtable] $headers) {
    $timeline = Invoke-Api "$base/Sandbox/_apis/build/builds/$runId/timeline?api-version=7.1" -Headers $headers -Allow 404
    if (-not $timeline) {
        return $false
    }

    return @($timeline.records | Where-Object { $_.result -eq 'failed' -and $_.log }).Count -gt 0
}

# GitLab CI

function Initialize-GitLab {
    $api = 'https://gitlab.com/api/v4'
    $group = Read-Value 'GitLab sandbox group path' (Get-Setting BUILDMONITOR_GITLAB_SCOPE_GROUP 'buildmonitor-sandbox')
    Request-Person @(
        'Create a temporary token for this script (Add new token, the legacy kind).',
        '  The name and the api scope are filled in. Expiration: tomorrow.'
    ) 'https://gitlab.com/-/user_settings/personal_access_tokens?name=BuildMonitor+setup&scopes=api'
    $setupToken = Read-Token 'the temporary GitLab token'
    $headers = @{ 'PRIVATE-TOKEN' = $setupToken }
    try {
        $groupPath = [uri]::EscapeDataString($group)
        $found = Invoke-Api "$api/groups/$groupPath" -Headers $headers -Allow 404
        if (-not $found) {
            Request-Person @("Create the group '$group': a blank group, with $group as both its name and its URL, visibility Private.") 'https://gitlab.com/groups/new#create-group-pane'
            $found = Invoke-Api "$api/groups/$groupPath" -Headers $headers
        }

        $project = Invoke-Api "$api/projects/$([uri]::EscapeDataString("$group/sandbox"))" -Headers $headers -Allow 404
        if (-not $project) {
            $project = Invoke-Api "$api/projects" -Method POST -Headers $headers -Body @{
                name = 'sandbox'
                path = 'sandbox'
                namespace_id = $found.id
                visibility = 'private'
                initialize_with_readme = $true
            }
            Write-Done "created $group/sandbox"
        }

        $branch = $project.default_branch ?? 'main'
        $files = "$api/projects/$($project.id)/repository/files/.gitlab-ci.yml"
        if (-not (Invoke-Api "${files}?ref=$branch" -Headers $headers -Allow 404)) {
            # The workflow rules run it only when started from the web or the API, so this commit
            # starts nothing.
            $null = Invoke-Api $files -Method POST -Headers $headers -Body @{
                branch = $branch
                content = (Format-File $gitlabCi)
                commit_message = 'Add .gitlab-ci.yml'
            }
            Write-Done 'wrote .gitlab-ci.yml'
        }

        $pipelines = "$api/projects/$($project.id)/pipelines"
        if (-not (Test-GitLabFailure $pipelines $headers)) {
            if (@(Invoke-Api "${pipelines}?per_page=1" -Headers $headers).Count -eq 0) {
                Request-Person @(
                    'GitLab runs pipelines only for accounts that have verified their identity.',
                    'If yours has not, the pipelines page shows a banner that starts it. Skip this if it has.'
                ) "https://gitlab.com/$group/sandbox/-/pipelines"
                $null = Invoke-Api "$api/projects/$($project.id)/pipeline" -Method POST -Headers $headers -Body @{ ref = $branch }
                Write-Done 'started a pipeline'
            }

            $null = Wait-For 'a failed pipeline' { Test-GitLabFailure $pipelines $headers }
        }

        # The permissions are named per tab rather than as one list. GitLab keeps them under a
        # Resource access selector of three tabs, and holds a token to the permissions of the tab
        # the call belongs to: a token carrying every project permission is still refused for
        # "user permissions: [User: Read]" while the User tab is empty.
        # Straight to the fine-grained form rather than the token list, which reaches it through a
        # Generate token menu. Nothing on it can be filled in from the URL: the legacy form still
        # takes name and scopes, and this one ignores both, so every field below is typed by hand.
        # The form is two passes: a resource is added, and then the permissions are set on it. The
        # box under the Resource access tabs says "Search for resources to add" and searches groups
        # and projects, not permissions, so a permission typed into it finds nothing. Nothing on the
        # form can be filled in from the URL either: the legacy form still takes name and scopes,
        # this one ignores both.
        Request-Person @(
            'Create the token the live tests use. Nothing here can be filled in from the link:',
            '  Name: BuildMonitor live tests. Expiration: up to a year.',
            "  Group and project access: Only specific groups or projects that I'm a member of.",
            "    Add group or project, then $group under Groups.",
            '  Add resource permissions: pick a tab under Resource access, add the resource, then set',
            '  its permissions in the table on the right. Each tab is counted on its own, so one left',
            '  empty is a permission the token lacks however full the others are:',
            "    Group and project: add $group, with Project read, Pipeline read and update, Job read.",
            '    User: add yourself, with User read and Personal access token read.',
            '    Global: nothing.'
        ) 'https://gitlab.com/-/user_settings/personal_access_tokens/granular/new'
        $token = Read-Token 'the GitLab token'
        $null = Invoke-Api "${pipelines}?per_page=1" -Headers @{ Authorization = "Bearer $token" }
        Write-Done 'the token reads the sandbox'

        Save-Settings ([ordered]@{ BUILDMONITOR_GITLAB_TOKEN = $token }) ([ordered]@{
            BUILDMONITOR_GITLAB_SCOPE_GROUP = $group
            BUILDMONITOR_GITLAB_PIPELINE = "$group/sandbox"
        })
    }
    finally {
        try {
            # A token can revoke itself.
            $null = Invoke-Api "$api/personal_access_tokens/self" -Method DELETE -Headers $headers -Allow 401, 404
            Write-Done 'revoked the temporary token'
        }
        catch {
            Write-Note 'Revoke the temporary token at https://gitlab.com/-/user_settings/personal_access_tokens'
        }
    }
}

function Test-GitLabFailure([string] $pipelines, [hashtable] $headers) {
    return @(Invoke-Api "${pipelines}?status=failed&per_page=1" -Headers $headers).Count -gt 0
}

# Bitbucket Pipelines

function Initialize-Bitbucket {
    $api = 'https://api.bitbucket.org/2.0'
    Request-Person @(
        'Sign in to Bitbucket as the sandbox account: an Atlassian account that belongs to the sandbox workspace only.',
        'Create the workspace if it does not exist (Create workspace).'
    ) 'https://bitbucket.org/account/workspaces/'
    $workspace = Read-Value 'Bitbucket sandbox workspace id' (Get-Setting BUILDMONITOR_BITBUCKET_SCOPE_WORKSPACE)
    $email = Read-Value "The sandbox account's email"
    Request-Person @(
        'Create a temporary API token for this script: Create API token with scopes, app Bitbucket, expiring tomorrow.',
        '  Scopes: read:workspace:bitbucket, read:repository:bitbucket, write:repository:bitbucket,',
        '  admin:repository:bitbucket, read:pipeline:bitbucket, write:pipeline:bitbucket, admin:pipeline:bitbucket.'
    ) 'https://id.atlassian.com/manage-profile/security/api-tokens'
    $setupToken = Read-Token 'the temporary Bitbucket token'
    $headers = @{ Authorization = Get-Basic $email $setupToken }
    try {
        $null = Invoke-Api "$api/workspaces/$workspace" -Headers $headers
        $repositoryApi = "$api/repositories/$workspace/sandbox"
        if (-not (Invoke-Api $repositoryApi -Headers $headers -Allow 404)) {
            # Bitbucket puts the repository in the workspace's oldest project, and refuses when the
            # account may not create repositories there. Then a person picks the project.
            $created = Invoke-Api $repositoryApi -Method POST -Headers $headers -Body @{ scm = 'git'; is_private = $true } -Allow 400, 403
            if ($created) {
                Write-Done "created $workspace/sandbox"
            }
            else {
                Request-Person @(
                    "Bitbucket would not let the script create the repository, so create it (this page):",
                    "  Workspace: $workspace. Project: any you may create repositories in. Repository name: sandbox.",
                    '  Access level: Private. Include a README: No. Default branch name: main.'
                ) 'https://bitbucket.org/repo/create'
                $null = Invoke-Api $repositoryApi -Headers $headers
            }
        }

        $config = Invoke-Api "$repositoryApi/pipelines_config" -Headers $headers -Allow 404
        if (-not $config -or -not $config.enabled) {
            $null = Invoke-Api "$repositoryApi/pipelines_config" -Method PUT -Headers $headers -Body @{ enabled = $true }
            Write-Done 'enabled Pipelines'
        }

        if (-not (Invoke-Api "$repositoryApi/src/main/bitbucket-pipelines.yml" -Headers $headers -Allow 404)) {
            # With Pipelines on, this commit starts the default pipeline.
            $null = Invoke-Api "$repositoryApi/src" -Method POST -Headers $headers -Form @{
                'bitbucket-pipelines.yml' = (Format-File $bitbucketPipelines)
                message = 'Add bitbucket-pipelines.yml'
                branch = 'main'
            }
            Write-Done 'wrote bitbucket-pipelines.yml'
        }

        $pipelines = "$repositoryApi/pipelines/?sort=-created_on&pagelen=20"
        if (-not (Test-BitbucketFailure $pipelines $headers)) {
            $null = Wait-For 'the pipeline the commit started' { @((Invoke-Api $pipelines -Headers $headers).values).Count -gt 0 } 2
            if (@((Invoke-Api $pipelines -Headers $headers).values).Count -eq 0) {
                $null = Invoke-Api "$repositoryApi/pipelines/" -Method POST -Headers $headers -Body @{
                    target = @{ type = 'pipeline_ref_target'; ref_type = 'branch'; ref_name = 'main' }
                }
                Write-Done 'started a pipeline'
            }

            $null = Wait-For 'a failed pipeline' { Test-BitbucketFailure $pipelines $headers }
        }

        Request-Person @(
            'Create the token the live tests use: Create API token with scopes, app Bitbucket, expiring in up to a year.',
            '  Scopes: read:workspace:bitbucket, read:repository:bitbucket, read:pipeline:bitbucket, write:pipeline:bitbucket.'
        ) 'https://id.atlassian.com/manage-profile/security/api-tokens'
        $token = Read-Token 'the Bitbucket token'
        $null = Invoke-Api "$repositoryApi/pipelines/?pagelen=1" -Headers @{ Authorization = Get-Basic $email $token }
        Write-Done 'the token reads the sandbox'

        Save-Settings ([ordered]@{
            BUILDMONITOR_BITBUCKET_TOKEN = $token
            BUILDMONITOR_BITBUCKET_USER = $email
        }) ([ordered]@{
            BUILDMONITOR_BITBUCKET_SCOPE_WORKSPACE = $workspace
            BUILDMONITOR_BITBUCKET_PIPELINE = "$workspace/sandbox"
        })
    }
    finally {
        # Bitbucket has no API to revoke an API token.
        Write-Note 'Revoke the temporary token at https://id.atlassian.com/manage-profile/security/api-tokens'
    }
}

function Test-BitbucketFailure([string] $pipelines, [hashtable] $headers) {
    $values = @((Invoke-Api $pipelines -Headers $headers).values)
    return [bool]($values | Where-Object { $_.state.name -eq 'COMPLETED' -and $_.state.result.name -eq 'FAILED' })
}

# AppVeyor

function Initialize-AppVeyor {
    $api = 'https://ci.appveyor.com/api'
    $owner = Read-Value 'GitHub organization to hold the AppVeyor sandbox repository' (Get-Setting BUILDMONITOR_GITHUB_SCOPE_OWNER)
    $slug = "$owner/sandbox-appveyor"
    Initialize-GhRepo $slug ([ordered]@{ 'appveyor.yml' = $appveyorYml })

    Request-Person @(
        'Sign in to AppVeyor and create a separate account for the sandbox, then switch to it.',
        "In that account, connect GitHub with access to $owner (first page, GitHub).",
        "Then copy that account's v1 API key, not the v2 one (second page)."
    ) @('https://ci.appveyor.com/projects/new', 'https://ci.appveyor.com/api-keys')
    $token = Read-Token 'the AppVeyor v1 API key'
    $headers = @{ Authorization = "Bearer $token" }

    $project = @(Invoke-Api "$api/projects" -Headers $headers) | Where-Object repositoryName -eq $slug | Select-Object -First 1
    if (-not $project) {
        $project = Invoke-Api "$api/projects" -Method POST -Headers $headers -Body @{ repositoryProvider = 'gitHub'; repositoryName = $slug }
        Write-Done "added $slug"
    }

    $history = "$api/projects/$($project.accountName)/$($project.slug)/history?recordsNumber=10"
    if (-not (Test-AppVeyorFailure $history $headers)) {
        if (@((Invoke-Api $history -Headers $headers).builds).Count -eq 0) {
            # The API that starts a build can answer 500 for a new account. A push reaches AppVeyor
            # through the webhook it added with the project instead.
            $started = Invoke-Api "$api/builds" -Method POST -Headers $headers -Allow 500 -Body @{
                accountName = $project.accountName
                projectSlug = $project.slug
                branch = 'main'
            }
            if ($started) {
                Write-Done 'started a build'
            }
            else {
                $trigger = Get-GhJson "repos/$slug/contents/build-trigger.txt"
                $body = @{
                    message = 'Start an AppVeyor build'
                    content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("$([DateTime]::UtcNow.ToString('O'))`n"))
                }
                if ($trigger) {
                    $body.sha = $trigger.sha
                }

                $null = Send-GhJson PUT "repos/$slug/contents/build-trigger.txt" $body
                Write-Done 'pushed a commit, which starts a build'
            }
        }

        $null = Wait-For 'a failed build' { Test-AppVeyorFailure $history $headers }
    }

    Save-Settings ([ordered]@{ BUILDMONITOR_APPVEYOR_TOKEN = $token }) ([ordered]@{ BUILDMONITOR_APPVEYOR_PIPELINE = $project.name })
}

function Test-AppVeyorFailure([string] $history, [hashtable] $headers) {
    return [bool](@((Invoke-Api $history -Headers $headers).builds) | Where-Object status -eq 'failed')
}

# Travis CI

function Initialize-Travis {
    $api = 'https://api.travis-ci.com'
    $default = $null
    $pipeline = Get-Setting BUILDMONITOR_TRAVIS_PIPELINE
    if ($pipeline) {
        $default = $pipeline.Split('/')[0]
    }

    $user = Read-Value 'The separate GitHub user that signs in to Travis' $default
    $slug = "$user/sandbox-travis"

    $userToken = & gh auth token --hostname github.com --user $user 2>$null
    if ($LASTEXITCODE -ne 0) {
        $yours = (Invoke-Gh api user --jq .login | Select-Object -First 1)
        Request-Person @(
            "gh signs in as $user next, in this window. When it shows a code, open https://github.com/login/device",
            "in a browser window signed in as $user (a private window works), and enter the code there.",
            "Your own gh sign-in ($yours) is switched back to afterwards."
        )
        & gh auth login --hostname github.com --web --git-protocol https
        $signedIn = $LASTEXITCODE -eq 0
        $null = & gh auth switch --hostname github.com --user $yours 2>&1
        if (-not $signedIn) {
            throw "gh could not sign in as $user"
        }

        $userToken = & gh auth token --hostname github.com --user $user 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "gh is not signed in as $user"
        }
    }

    # As that user for this repository only. The active gh account stays yours.
    $env:GH_TOKEN = "$userToken".Trim()
    try {
        Initialize-GhRepo $slug ([ordered]@{ '.travis.yml' = $travisYml })
    }
    finally {
        Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
    }

    Request-Person @(
        "Open these pages in a browser window signed in to GitHub as $user, not in your usual one:",
        '  1. Sign in to Travis CI with GitHub, and choose a plan: the usage based plan, or open source',
        '     credits, which Travis support grants on request.',
        "  2. Install the Travis CI GitHub App on $user, with Only select repositories: $slug.",
        '  3. Sync account, so Travis lists the repository.',
        '  4. Copy the API token.'
    ) @(
        'https://app.travis-ci.com/signin',
        'https://github.com/apps/travis-ci/installations/new',
        'https://app.travis-ci.com/account/repositories',
        'https://app.travis-ci.com/account/preferences'
    ) -Show
    $token = Read-Token 'the Travis token'
    $headers = @{ 'Travis-API-Version' = '3'; Authorization = "token $token" }

    # A token copied in the usual browser acts as you, on every repository you have.
    $login = (Invoke-Api "$api/user" -Headers $headers).login
    if ($login -ne $user) {
        throw "That token belongs to $login, not $user. Copy it again in the window signed in as $user."
    }

    $repositoryApi = "$api/repo/$([uri]::EscapeDataString($slug))"
    $found = Invoke-Api $repositoryApi -Headers $headers -Allow 404
    if (-not $found) {
        # Travis learns about a repository the app was just given on its next sync.
        $account = Invoke-Api "$api/user" -Headers $headers
        $null = Invoke-Api "$api/user/$($account.id)/sync" -Method POST -Headers $headers -Allow 409
        $null = Wait-For "Travis to see $slug" { $null -ne (Invoke-Api $repositoryApi -Headers $headers -Allow 404) } 5
        $found = Invoke-Api $repositoryApi -Headers $headers -Allow 404
        if (-not $found) {
            throw "Travis does not list $slug. Check the Travis CI GitHub App on $user has access to it, then Sync account."
        }
    }

    if (-not $found.active) {
        $null = Invoke-Api "$repositoryApi/activate" -Method POST -Headers $headers
        Write-Done "activated $slug"
    }

    $builds = "$repositoryApi/builds?limit=10"
    if (-not (Test-TravisFailure $builds $headers)) {
        if (@((Invoke-Api $builds -Headers $headers).builds).Count -eq 0) {
            $null = Invoke-Api "$repositoryApi/requests" -Method POST -Headers $headers -Body @{
                request = @{ branch = 'main'; message = 'BuildMonitor sandbox' }
            }
            Write-Done 'requested a build'
        }

        $null = Wait-For 'a failed build' { Test-TravisFailure $builds $headers }
    }

    Save-Settings ([ordered]@{ BUILDMONITOR_TRAVIS_TOKEN = $token }) ([ordered]@{ BUILDMONITOR_TRAVIS_PIPELINE = $slug })
}

function Test-TravisFailure([string] $builds, [hashtable] $headers) {
    return [bool](@((Invoke-Api $builds -Headers $headers).builds) | Where-Object state -eq 'failed')
}

# Octopus Deploy

function Initialize-Octopus {
    $server = Read-Host '>> Octopus server, such as https://example.octopus.app (leave blank to create a free Cloud instance first)'
    if ([string]::IsNullOrWhiteSpace($server)) {
        Request-Person @('Create a free Octopus Cloud instance.') 'https://octopus.com/start'
        $server = Read-Value 'Octopus server'
    }

    # As the settings want it: lower case, no trailing slash, and with its scheme, which a pasted
    # host name leaves out.
    $server = $server.Trim().TrimEnd('/').ToLowerInvariant()
    if ($server -notmatch '^https?://') {
        $server = "https://$server"
    }
    Request-Person @(
        'Create a temporary API key for this script: your user menu > Profile > My API Keys > New API key.',
        '  Purpose: BuildMonitor setup, so the script can revoke it. Expires: tomorrow.'
    ) "$server/app#/users/me/apiKeys"
    $setupKey = Read-Token 'the temporary Octopus API key'
    $headers = @{ 'X-Octopus-ApiKey' = $setupKey }
    $me = $null
    try {
        $me = Invoke-Api "$server/api/users/me" -Headers $headers
        $spaceName = Read-Value 'Octopus sandbox space' (Get-Setting BUILDMONITOR_OCTOPUS_SCOPE_SPACE 'Default')
        $space = Find-Octopus "$server/api/spaces/all" $spaceName $headers
        if (-not $space) {
            $space = Invoke-Api "$server/api/spaces" -Method POST -Headers $headers -Body @{
                Name = $spaceName
                SpaceManagersTeamMembers = @($me.Id)
                SpaceManagersTeams = @()
                IsDefault = $false
                TaskQueueStopped = $false
            }
            Write-Done "created the space $spaceName"
        }

        $spaceApi = "$server/api/$($space.Id)"
        $sandboxEnvironment = Find-Octopus "$spaceApi/environments/all" 'Sandbox' $headers
        if (-not $sandboxEnvironment) {
            $sandboxEnvironment = Invoke-Api "$spaceApi/environments" -Method POST -Headers $headers -Body @{ Name = 'Sandbox'; UseGuidedFailure = $false }
            Write-Done 'created the environment Sandbox'
        }

        $project = Find-Octopus "$spaceApi/projects/all" 'BuildMonitor Sandbox' $headers
        if (-not $project) {
            $lifecycles = @(Invoke-Api "$spaceApi/lifecycles/all" -Headers $headers)
            $lifecycle = ($lifecycles | Where-Object Name -eq 'Default Lifecycle' | Select-Object -First 1) ?? $lifecycles[0]
            $group = @(Invoke-Api "$spaceApi/projectgroups/all" -Headers $headers)[0]
            # Guided failure off, or a failed deployment waits for a person.
            $project = Invoke-Api "$spaceApi/projects" -Method POST -Headers $headers -Body @{
                Name = 'BuildMonitor Sandbox'
                LifecycleId = $lifecycle.Id
                ProjectGroupId = $group.Id
                DefaultGuidedFailureMode = 'Off'
            }
            Write-Done 'created the project BuildMonitor Sandbox'
        }

        $processUri = "$server$($project.Links.DeploymentProcess)"
        $process = Invoke-Api $processUri -Headers $headers
        if (@($process.Steps).Count -eq 0) {
            $pools = @(Invoke-Api "$spaceApi/workerpools/all" -Headers $headers)
            $pool = $pools | Where-Object { $_.WorkerPoolType -eq 'DynamicWorkerPool' -and "$($_.WorkerType)" -like 'Ubuntu*' } | Select-Object -First 1
            if (-not $pool) {
                $pool = $pools | Where-Object IsDefault | Select-Object -First 1
            }

            $process.Steps = @(@{
                Name = 'Fail after a minute'
                Condition = 'Success'
                StartTrigger = 'StartAfterPrevious'
                PackageRequirement = 'LetOctopusDecide'
                Properties = @{}
                Actions = @(@{
                    Name = 'Fail after a minute'
                    ActionType = 'Octopus.Script'
                    WorkerPoolId = $pool.Id
                    Environments = @()
                    ExcludedEnvironments = @()
                    Channels = @()
                    TenantTags = @()
                    Packages = @()
                    Properties = @{
                        'Octopus.Action.RunOnServer' = 'true'
                        'Octopus.Action.Script.ScriptSource' = 'Inline'
                        'Octopus.Action.Script.Syntax' = 'Bash'
                        'Octopus.Action.Script.ScriptBody' = (Format-File $octopusScript)
                    }
                })
            })
            $null = Invoke-Api $processUri -Method PUT -Headers $headers -Body $process
            Write-Done 'added the Run a Script step'
        }

        $release = @((Invoke-Api "$spaceApi/projects/$($project.Id)/releases" -Headers $headers).Items) | Where-Object Version -eq '0.0.1' | Select-Object -First 1
        if (-not $release) {
            $release = Invoke-Api "$spaceApi/releases" -Method POST -Headers $headers -Body @{ ProjectId = $project.Id; Version = '0.0.1' }
            Write-Done 'created release 0.0.1'
        }

        $deployments = "$spaceApi/deployments?projects=$($project.Id)&take=5"
        if (-not (Test-OctopusFailure $server $deployments $headers)) {
            if (@((Invoke-Api $deployments -Headers $headers).Items).Count -eq 0) {
                $null = Invoke-Api "$spaceApi/deployments" -Method POST -Headers $headers -Body @{
                    ReleaseId = $release.Id
                    EnvironmentId = $sandboxEnvironment.Id
                }
                Write-Done 'deployed 0.0.1 to Sandbox'
            }

            $null = Wait-For 'a failed deployment' { Test-OctopusFailure $server $deployments $headers }
        }

        $service = Find-Octopus "$server/api/users/all" 'buildmonitor-sandbox' $headers 'Username'
        if (-not $service) {
            $service = Invoke-Api "$server/api/users" -Method POST -Headers $headers -Body @{
                Username = 'buildmonitor-sandbox'
                DisplayName = 'BuildMonitor live tests'
                IsService = $true
                IsActive = $true
            }
            Write-Done 'created the service account buildmonitor-sandbox'
        }

        $roles = @(Invoke-Api "$server/api/userroles/all" -Headers $headers)
        $cancel = $roles | Where-Object Name -eq 'BuildMonitor task cancel' | Select-Object -First 1
        if (-not $cancel) {
            $cancel = Invoke-Api "$server/api/userroles" -Method POST -Headers $headers -Body @{
                Name = 'BuildMonitor task cancel'
                Description = 'Cancels the deployments the BuildMonitor live tests start.'
                GrantedSpacePermissions = @('TaskCancel')
                GrantedSystemPermissions = @()
            }
            Write-Done 'created the role BuildMonitor task cancel'
        }

        $wanted = @(
            ($roles | Where-Object Name -eq 'Project viewer' | Select-Object -First 1),
            ($roles | Where-Object Name -eq 'Deployment creator' | Select-Object -First 1),
            $cancel)
        if ($wanted -contains $null) {
            throw 'The built-in roles Project viewer and Deployment creator were not found'
        }

        $team = Find-Octopus "$spaceApi/teams/all" 'BuildMonitor Sandbox' $headers
        if (-not $team) {
            $team = Invoke-Api "$spaceApi/teams" -Method POST -Headers $headers -Body @{
                Name = 'BuildMonitor Sandbox'
                SpaceId = $space.Id
                MemberUserIds = @($service.Id)
                ExternalSecurityGroups = @()
            }
            Write-Done 'created the team BuildMonitor Sandbox'
        }
        elseif (@($team.MemberUserIds) -notcontains $service.Id) {
            $team.MemberUserIds = @($team.MemberUserIds) + $service.Id
            $team = Invoke-Api "$spaceApi/teams/$($team.Id)" -Method PUT -Headers $headers -Body $team
            Write-Done 'added the service account to the team'
        }

        $scoped = @((Invoke-Api "$server$($team.Links.ScopedUserRoles -replace '\{.*\}$', '')" -Headers $headers).Items)
        foreach ($role in $wanted) {
            if ($scoped | Where-Object UserRoleId -eq $role.Id) {
                continue
            }

            $null = Invoke-Api "$spaceApi/scopeduserroles" -Method POST -Headers $headers -Body @{
                UserRoleId = $role.Id
                TeamId = $team.Id
                SpaceId = $space.Id
                ProjectIds = @($project.Id)
                EnvironmentIds = @($sandboxEnvironment.Id)
                ProjectGroupIds = @()
                TenantIds = @()
            }
            Write-Done "gave the team $($role.Name), limited to the sandbox"
        }

        # The service account's key is made here, so it is never shown to anyone.
        $keys = "$server/api/users/$($service.Id)/apikeys"
        $created = Invoke-Api $keys -Method POST -Headers $headers -Body @{ Purpose = 'BuildMonitor live tests' }
        $token = $created.ApiKey
        if ($token -isnot [string] -or -not $token.StartsWith('API-')) {
            throw 'Octopus did not return the new API key'
        }

        $null = Invoke-Api "$spaceApi/projects/$($project.Id)" -Headers @{ 'X-Octopus-ApiKey' = $token }
        Write-Done 'the service account reads the sandbox'

        Save-Settings ([ordered]@{
            BUILDMONITOR_OCTOPUS_TOKEN = $token
            BUILDMONITOR_OCTOPUS_SERVER = $server
        }) ([ordered]@{
            BUILDMONITOR_OCTOPUS_SCOPE_SPACE = $spaceName
            BUILDMONITOR_OCTOPUS_PIPELINE = 'BuildMonitor Sandbox'
            BUILDMONITOR_OCTOPUS_ENVIRONMENT = 'Sandbox'
        })

        # The key just saved replaces any the service account had.
        foreach ($old in @((Invoke-Api "${keys}?take=100" -Headers $headers).Items)) {
            if ($old.Id -ne $created.Id) {
                $null = Invoke-Api "$keys/$($old.Id)" -Method DELETE -Headers $headers
                Write-Done 'revoked an older key of the service account'
            }
        }
    }
    finally {
        Remove-OctopusSetupKey $server $me $headers
    }
}

function Find-Octopus([string] $uri, [string] $name, [hashtable] $headers, [string] $property = 'Name') {
    return @(Invoke-Api $uri -Headers $headers) | Where-Object { $_.$property -eq $name } | Select-Object -First 1
}

function Test-OctopusFailure([string] $server, [string] $deployments, [hashtable] $headers) {
    foreach ($deployment in @((Invoke-Api $deployments -Headers $headers).Items)) {
        $task = Invoke-Api "$server$($deployment.Links.Task)" -Headers $headers
        if ($task.State -eq 'Failed') {
            return $true
        }
    }

    return $false
}

function Remove-OctopusSetupKey([string] $server, $me, [hashtable] $headers) {
    try {
        if (-not $me) {
            throw 'not signed in'
        }

        $keys = "$server/api/users/$($me.Id)/apikeys"
        $setup = @((Invoke-Api "${keys}?take=100" -Headers $headers).Items) | Where-Object Purpose -eq 'BuildMonitor setup'
        if (-not $setup) {
            throw 'no key has the purpose BuildMonitor setup'
        }

        foreach ($key in $setup) {
            $null = Invoke-Api "$keys/$($key.Id)" -Method DELETE -Headers $headers
        }

        Write-Done 'revoked the temporary key'
    }
    catch {
        Write-Note "Revoke the temporary key at $server/app#/users/me/apiKeys"
    }
}

# The run

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'This needs the GitHub CLI: https://cli.github.com'
}

if ($Local -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '-Local needs the .NET SDK'
}

& gh auth status --hostname github.com *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Sign gh in first: gh auth login'
}

$providers = [ordered]@{
    'github' = @{ Id = 'GITHUB'; Name = 'GitHub Actions' }
    'azure-devops' = @{ Id = 'AZURE_DEVOPS'; Name = 'Azure DevOps' }
    'gitlab' = @{ Id = 'GITLAB'; Name = 'GitLab CI' }
    'bitbucket' = @{ Id = 'BITBUCKET'; Name = 'Bitbucket Pipelines' }
    'appveyor' = @{ Id = 'APPVEYOR'; Name = 'AppVeyor' }
    'travis' = @{ Id = 'TRAVIS'; Name = 'Travis CI' }
    'octopus' = @{ Id = 'OCTOPUS'; Name = 'Octopus Deploy' }
}

Initialize-Environment
$secretNames = Get-SecretNames
$failures = @()
foreach ($id in $providers.Keys) {
    if ($Provider -notcontains $id) {
        continue
    }

    $details = $providers[$id]
    Write-Section $details.Name
    if ($secretNames -contains "BUILDMONITOR_$($details.Id)_TOKEN" -and
        -not (Read-YesNo "$($details.Name) already has a token. Set it up again and replace the token?")) {
        Write-Done 'left as it is'
        continue
    }

    try {
        switch ($id) {
            'github' { Initialize-GitHub }
            'azure-devops' { Initialize-AzureDevOps }
            'gitlab' { Initialize-GitLab }
            'bitbucket' { Initialize-Bitbucket }
            'appveyor' { Initialize-AppVeyor }
            'travis' { Initialize-Travis }
            'octopus' { Initialize-Octopus }
        }
    }
    catch {
        # With the line it stopped on. A message from inside a framework call, such as a URI the web
        # client could not parse, says nothing about which of a hundred calls asked for it, and the
        # setup is too long to bisect by rerunning it.
        Write-Host "   $($details.Name) stopped: $_" -ForegroundColor Red
        if ($_.InvocationInfo) {
            Write-Host "   at line $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor Red
        }

        $failures += $details.Name
    }
}

# Runs for all cover the providers that have a token, since one without a sandbox fails.
Write-Section 'Providers the Live workflow runs'
$secretNames = Get-SecretNames
$hosted = @($providers.Keys | Where-Object { $secretNames -contains "BUILDMONITOR_$($providers[$_].Id)_TOKEN" })
$json = ConvertTo-Json -InputObject $hosted -Compress
$output = & gh variable set BUILDMONITOR_LIVE_HOSTED --repo $Repository --body $json 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Setting BUILDMONITOR_LIVE_HOSTED failed: $output"
}

Write-Done "BUILDMONITOR_LIVE_HOSTED = $json"

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "Not finished: $($failures -join ', '). Fix what was said above, then run again with -Provider." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "Done. Start the Live workflow with: gh workflow run live.yml --repo $Repository" -ForegroundColor Cyan
