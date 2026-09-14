# Azure DevOps

Watches the pipelines of one organization, across every project or one named project.


## Credential

A [personal access token](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate) with Build: Read & execute. Or sign in through Microsoft Entra ID, once an application is registered; see [Authentication](../auth.md). Personal Microsoft accounts cannot sign in that way.

For Azure DevOps Server enter the server URL and the collection as the organization.


## Rows

One per pipeline definition. Pull request builds show the pull request number and link to it, on Azure Repos and on GitHub repositories.


## Actions

 * Retry re-runs the build
 * Cancel cancels a queued or running build


## Estimates

The countdown comes from the median of the pipeline's last ten successful runs.


## Polling

Builds are fetched a project at a time, each on its own schedule (see [Poll intervals](../options.md#poll-intervals)). Azure DevOps sends no ETags, so every request is a full one, and it allows each user 200 throughput units in any five minutes. Requests are budgeted to half of that by the cost Azure DevOps reports on each response, and a pause it asks for is honoured before it starts delaying requests.

Each project asks for the last five builds of every definition, so a busy definition cannot push a quiet one out of the list. Each poll interval, each project is also asked only for builds queued since the newest one seen, which costs a small fraction of a fetch, so a new build on a quiet project shows within about thirty seconds. A retry started outside the tray reuses its build, and may wait for the schedule.


## API notes

Researched 2026-09-14. [live] means checked with anonymous requests against the public dnceng-public organization; [docs] names the evidence. See [Provider APIs](api-comparison.md) for every provider side by side.


### Rate limits

 * Consumption is measured in throughput units (TSTU): 200 TSTU in any sliding five minutes per user, with each pipeline tracked separately. A typical user uses about 1 TSTU per five minutes [docs].
 * Past the limit, requests are first delayed by up to 30 seconds each, then blocked with a 429 and `TF400733` [docs].
 * `X-RateLimit-Cost` comes on every response, in TSTU to five decimal places [live]. `X-RateLimit-Limit`, `X-RateLimit-Remaining` and `X-RateLimit-Reset` (Unix seconds) come only as usage nears the limit, `X-RateLimit-Delay` once a request was delayed, and `X-RateLimit-Resource` is a label for people. `Retry-After` can arrive on a 200 [docs].
 * Only the Basic + Test Plans access level raises the limit [docs].

Measured costs [live]:

| Request | TSTU |
|---|---|
| builds, `$top=5` | 0.027 to 0.049 |
| builds, `$top=200` (1.16 MB) | 0.183 |
| builds, five `definitions` with `maxBuildsPerDefinition=2` | 0.052 |
| builds, `minTime` matching nothing | 0.0016 to 0.0024 |
| definitions, `includeLatestBuilds&$top=3` | 0.041 |
| pipelines, `$top=3` | 0.0056 |


### Conditional requests

None. Builds, definitions and pipelines lists send no ETag or Last-Modified, and `Cache-Control: no-cache, no-store, must-revalidate` [live]. Every request is a full, billed one.


### Change detection

 * `{project}/_apis/build/builds?minTime=…&queryOrder=queueTimeDescending` returns only builds queued after `minTime`. `minTime` applies to the queue, start or finish time according to `queryOrder` [docs]. An empty answer costs about 0.002 TSTU [live].
 * Rejected: a project's `lastUpdateTime` is metadata only, dated 2023 on a project building today [live]; git pushes need the `vso.code` scope and cover Azure Repos only; the Runs API has no top or time filter and returns up to 10,000 runs; service hooks need a public endpoint [docs].


### Batching

 * `maxBuildsPerDefinition=N` together with `definitions=` returns exactly N builds per definition, including definitions idle for years [live].
 * `$top` alone fills with the busiest definitions: 200 builds covered only 42 definitions [live].
 * An organization level `_apis/build/builds`, without a project in the path, exists in the .NET client but is unverified over REST; anonymous calls are redirected to sign in.


### Quirks

 * A personal access token that is wrong or expired is answered with a 302 to `…vssps.visualstudio.com/_signin` [live], or a 203 with an HTML page [docs]. A client that follows redirects sees HTML, not a 401.
 * The builds list needs a project in the path [docs].


### Sources

 * [Rate and usage limits](https://learn.microsoft.com/en-us/azure/devops/integrate/concepts/rate-limits)
 * [Builds - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-7.1)
 * [Definitions - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/definitions/list?view=azure-devops-rest-7.1)
 * [Pushes - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pushes/list?view=azure-devops-rest-7.1)
 * [Runs - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/list?view=azure-devops-rest-7.1)
 * [BuildHttpClientBase.GetBuildsAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.build.webapi.buildhttpclientbase.getbuildsasync?view=azure-devops-dotnet)
 * [HTTP 203 from the REST API](https://learn.microsoft.com/en-us/answers/questions/559772/azure-devops-rest-api-keep-getting-http-203)
