# Octopus Deploy

Watches the deployments of every project in one space: the default space, or the one the connection names.


## Credential

An [API key](https://octopus.com/docs/api/authentication/create-an-api-key), created under Profile, My API Keys.


## Rows

One per project, showing the latest deployment. The environment stands in for the branch and the release version for the run number.


## Actions

 * Retry re-runs the deployment task
 * Cancel cancels a queued or executing task
 * Copy log copies the deployment task's log


## Estimates

Octopus reports the progress and the estimated time remaining of an executing task, and that drives the bar and the countdown.


## Polling

One dashboard request returns the current and previous deployment of every project to every environment, instead of separate requests for environments, deployments and tasks, so the whole space is fetched on one schedule (see [Poll intervals](../options.md#poll-intervals)). An executing deployment costs one more request, for its progress. When a large server limits how many projects its dashboard returns, the separate requests are used instead.

```mermaid
---
config:
  flowchart:
    wrappingWidth: 400
---
flowchart TD
    wake(["Wake: the connection is due,<br/>or Refresh, Retry or Cancel"]) --> listed{"Listed projects in<br/>the last 10 minutes?"}
    listed -- "no" --> discover["GET spaces, then the<br/>projects of the default<br/>or named space"]
    listed -- "yes" --> interval["One schedule for every<br/>project, set by the busiest"]
    discover --> interval
    interval --> finishing["A deployment executing<br/>with 90 s or less left, or<br/>no estimate: every 10 s"]
    interval --> running["Any other executing<br/>or queued deployment:<br/>every 30 s"]
    interval --> quiet["Otherwise the shortest of<br/>each project's time since<br/>its last deployment ÷ 30,<br/>or ÷ 120 when that failed,<br/>30 s to 5 minutes"]
    finishing --> due{"Due?"}
    running --> due
    quiet --> due
    due -- "no" --> sleep(["Sleep until the connection<br/>or the listing is due"])
    due -- "yes" --> fetch["GET {space}/dashboard/dynamic,<br/>the current and previous<br/>deployment of every project<br/>to every environment"]
    fetch -- "200" --> limited{"Cut short by<br/>ProjectLimit?"}
    limited -- "yes" --> separately["GET environments,<br/>deployments and<br/>tasks instead"]
    limited -- "no" --> executing{"A deployment<br/>executing?"}
    separately --> executing
    executing -- "yes" --> details["GET its task details<br/>for progress and time left"]
    executing -- "no" --> rows["Update the rows"]
    details --> rows
    fetch -- "failure" --> backoff["Back off the connection,<br/>doubling up to 10 minutes"]
    rows --> sleep
    backoff --> sleep
```


## API notes

Researched 2026-09-14. [live] means checked with anonymous requests against the Octopus Cloud samples instance, which refuses API reads without a key; [docs] names the evidence. See [Provider APIs](api-comparison.md) for every provider side by side.


### Rate limits

None published, and no rate limit headers [live]. `Server-Timing: total;dur=…` shows how long the server took.


### Conditional requests

None seen: the API root sends `Cache-Control: private` and no ETag [live]. Endpoints behind a key are untested.


### Batching

 * `/api/{space}/dashboard/dynamic{?projects,environments,includePrevious}` returns the current and previous deployment for every project and environment in one request, each with `ProjectId`, `EnvironmentId`, `TenantId`, `ReleaseVersion`, `DeploymentId`, `TaskId`, `State` and times [docs]. It has no task description and no rerun or cancel links, and `ProjectLimit` and `IsFiltered` say when it was cut short.
 * `tasks?ids=a,b,c` fetches several tasks in one request [live].


### Change detection

 * `tasks?name=Deploy&take=1` returns the newest deployment task, and `TotalCounts` per state.
 * `/api/events?spaces=…&eventCategories=DeploymentQueued,DeploymentStarted,DeploymentSucceeded,DeploymentFailed&from=…` lists deployment events, and needs the EventView permission [docs].


### Quirks

Links are URI templates. A task's details link is `Details{?verbose,tail,ranges}` [docs], so expand or strip the template before requesting it. `verbose=false` and a small `tail` shrink the response, which otherwise carries the whole log tree.


### Sources

 * [REST API](https://octopus.com/docs/octopus-rest-api)
 * [Auditing](https://octopus.com/docs/security/users-and-teams/auditing)
 * Go client: [dashboard](https://pkg.go.dev/github.com/OctopusDeploy/go-octopusdeploy/v2/pkg/dashboard), [tasks](https://pkg.go.dev/github.com/OctopusDeploy/go-octopusdeploy/v2/pkg/tasks) and [events](https://pkg.go.dev/github.com/OctopusDeploy/go-octopusdeploy/v2/pkg/events)
 * [OctopusClients](https://github.com/OctopusDeploy/OctopusClients): `TaskResourceCollection.cs`, `TaskRepository.cs` and its canned task responses
 * [Dashboard performance on large installs](https://github.com/OctopusDeploy/Issues/issues/2850)
