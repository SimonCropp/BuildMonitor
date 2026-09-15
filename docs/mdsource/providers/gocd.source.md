# GoCD

Watches every pipeline on the server.


## Credential

A [personal access token](https://api.gocd.org/current/#access-tokens), created at `https://<server>/go/access_tokens`.


## Rows

One per pipeline. The stages of an instance fold into one status: building while any stage builds, failed or cancelled when one is, passed when every scheduled stage passed. The branch comes from the git material.


## Actions

 * Retry re-runs the failed jobs of the failed stage, or schedules the pipeline when nothing failed
 * Cancel cancels the running stage
 * Copy log copies the consoles of the failed jobs of the failed stages


## Estimates

The countdown comes from the median of the pipeline's last ten successful instances.


## Polling

Each pipeline's history is fetched on its own schedule (see [Poll intervals](../options.md#poll-intervals)), and history carries no ETag, so each request returns a full response. Each poll interval the dashboard is read instead, with a conditional request that answers 304 while nothing visible changed. A pipeline whose instance counter or stage statuses moved is fetched at once. Progress inside a running job does not change the dashboard, and waits for the schedule.

```mermaid
---
config:
  flowchart:
    wrappingWidth: 400
---
flowchart TD
    wake(["Wake: something is due,<br/>or Refresh, Retry or Cancel"]) --> listed{"Listed pipelines in<br/>the last 10 minutes?"}
    listed -- "no" --> discover["GET dashboard, keeping<br/>the known pipelines<br/>while it answers 202"]
    listed -- "yes" --> probed{"Probed in the<br/>last 30 seconds?"}
    probed -- "no" --> probe["GET dashboard with<br/>If-None-Match, a 304 while<br/>nothing visible changed"]
    probe --> moved{"A pipeline's counter<br/>or stage statuses moved?"}
    moved -- "yes" --> nudge["Fetch that pipeline<br/>now, then every 30 s<br/>for 3 minutes"]
    discover --> interval["Each pipeline is polled<br/>as its latest instances need"]
    probed -- "yes" --> interval
    moved -- "no" --> interval
    nudge --> interval
    interval --> finishing["Building, from its fastest<br/>recent instance to 90 s past<br/>its slowest, or with no<br/>history: every 10 s"]
    interval --> running["Building for less than its<br/>fastest recent instance, or<br/>scheduled: every 30 s, and<br/>again when it reaches that"]
    interval --> overrun["Building over 90 s past<br/>its slowest recent instance:<br/>the time beyond that ÷ 10,<br/>30 s to 5 minutes"]
    interval --> quiet["Quiet: the time since<br/>the last instance ÷ 30,<br/>30 s to 5 minutes"]
    interval --> failed["Quiet after a failure:<br/>the time since the<br/>last instance ÷ 120,<br/>30 s to 5 minutes"]
    finishing --> due{"Due?"}
    running --> due
    overrun --> due
    quiet --> due
    failed --> due
    due -- "no" --> sleep(["Sleep until a pipeline,<br/>the probe or the<br/>listing is due"])
    due -- "yes, most urgent first" --> fetch["GET pipelines/{name}/history<br/>?page_size=10,<br/>keeping the newest 5"]
    fetch -- "200" --> rows["Update its rows"]
    fetch -- "failure" --> backoff["Back off that pipeline,<br/>doubling up to 10 minutes"]
    rows --> sleep
    backoff --> sleep
```


## API notes

Researched 2026-09-14. No public GoCD server allows anonymous API reads, so these come from the API reference [docs] and the server source [source]. See [Provider APIs](api-comparison.md) for every provider side by side.


### Rate limits

None, and no rate limit headers [source].


### Conditional requests

 * The dashboard sends a quoted ETag, a digest of the user and each visible pipeline's name and last update. `If-None-Match` is compared without the quotes and without a `--gzip` suffix, and a match answers 304 [source].
 * Pipeline history sends no ETag [source].


### Change detection

The dashboard holds each pipeline's latest instance, plus any instance with a building or failing stage, with counters and stage statuses [source]. It follows the user's saved dashboard view. A configuration change bumps every pipeline's last update, so compare counters rather than timestamps. Progress within a job does not change the ETag.


### Batching

None for history, which is per pipeline. The dashboard has no branch, commit, author or finish time.


### Quirks

 * `history?page_size` must be between 10 and 100. Anything else is a 400: "The query parameter 'page_size', if specified must be a number between 10 and 100." Paging continues with `after` and `before` cursors [source].
 * The dashboard answers 202 until its cache has loaded [source].
 * `Accept: application/vnd.go.cd+json` without a version gets the latest version of each API, from 19.8 [docs], so pin versions where the shape matters.
 * A failing stage, where a job failed while others still run, reports the result Failed with a status other than Building [source].


### Sources

 * [Dashboard](https://api.gocd.org/current/#dashboard), [pipeline history](https://api.gocd.org/current/#get-pipeline-history) and [API versions](https://api.gocd.org/current/#api-versions)
 * Source: [ApiController.java](https://github.com/gocd/gocd/blob/master/api/api-base/src/main/java/com/thoughtworks/go/api/ApiController.java), [PipelineInstanceControllerV1.java](https://github.com/gocd/gocd/blob/master/api/api-pipeline-instance-v1/src/main/java/com/thoughtworks/go/apiv1/pipelineinstance/PipelineInstanceControllerV1.java) and the [dashboard API](https://github.com/gocd/gocd/tree/master/api/api-dashboard-v4)
