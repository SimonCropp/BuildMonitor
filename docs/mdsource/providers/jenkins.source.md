# Jenkins

Watches every job on the server, walking folders and multibranch projects. A branch of a multibranch project is a pipeline of its own, and a branch job named `PR-n` is shown as pull request n.


## Credential

An [API token](https://www.jenkins.io/doc/book/system-administration/authenticating-scripted-clients/), created at `https://<server>/me/security`, together with the Jenkins user name.


## Rows

One per job. A job with a queued build shows a queued row until it starts.


## Actions

 * Retry queues a new build of the job; Jenkins has no rerun. A parameterized job is built with its default parameters
 * Cancel stops a running build, or removes a queued one from the queue
 * Copy log copies the build's console


## Estimates

Jenkins reports an estimated duration for every build, from its own history, and that drives the countdown.


## Polling

Each job is fetched on its own schedule (see [Poll intervals](../options.md#poll-intervals)). Jenkins sends no ETags, and fetching a job reads its last builds from disk, so each poll interval one request reads the job tree instead, with each job's next build number and whether a build is queued. A job with a new or queued build is fetched at once; the rest wait for their schedule. Jobs nested deeper than three folders are not in that request, so they wait for the schedule too.

```mermaid
---
config:
  flowchart:
    wrappingWidth: 400
---
flowchart TD
    wake(["Wake: something is due,<br/>or Refresh, Retry or Cancel"]) --> listed{"Listed jobs in<br/>the last 10 minutes?"}
    listed -- "no" --> discover["GET api/json?tree=jobs[…]<br/>three levels deep, then<br/>each deeper folder"]
    listed -- "yes" --> probed{"Probed in the<br/>last 30 seconds?"}
    probed -- "no" --> probe["GET api/json?tree=jobs[url,<br/>nextBuildNumber,inQueue,…]<br/>three levels deep,<br/>reading no builds"]
    probe --> moved{"A job's next build<br/>number or queue<br/>state moved?"}
    moved -- "yes" --> nudge["Fetch that job<br/>now, then every 30 s<br/>for 3 minutes"]
    discover --> interval["Each job is polled<br/>as its latest builds need"]
    probed -- "yes" --> interval
    moved -- "no" --> interval
    nudge --> interval
    interval --> finishing["Building, past ¾ of<br/>Jenkins' estimate or<br/>with none: every 10 s"]
    interval --> running["Building for less than<br/>that, or queued:<br/>every 30 s"]
    interval --> quiet["Quiet: the time since<br/>the last build ÷ 30,<br/>30 s to 5 minutes"]
    interval --> failed["Quiet after a failed or<br/>unstable build: the time<br/>since it ÷ 120,<br/>30 s to 5 minutes"]
    finishing --> due{"Due?"}
    running --> due
    quiet --> due
    failed --> due
    due -- "no" --> sleep(["Sleep until a job,<br/>the probe or the<br/>listing is due"])
    due -- "yes, most urgent first" --> fetch["GET {job}/api/json<br/>?tree=builds[…]{0,5},inQueue"]
    fetch -- "200" --> rows["Update its rows"]
    fetch -- "failure" --> backoff["Back off that job,<br/>doubling up to 10 minutes"]
    rows --> sleep
    backoff --> sleep
```


## API notes

Researched 2026-09-14. [live] means checked with anonymous requests against ci-builds.apache.org; [docs] and [source] name the evidence. See [Provider APIs](api-comparison.md) for every provider side by side.


### Rate limits

None, and no rate limit headers [live]. The cost is load on the controller: the remote API help recommends `tree` and treats `depth` as for exploring only [source].


### Conditional requests

None. `api/json` sends `X-Jenkins`, `X-Jenkins-Session`, `X-Content-Type-Options`, `X-Frame-Options` and `Cache-Control: private`, and answers `If-None-Match` and `If-Modified-Since` with a full 200 [source, live].


### Change detection

 * A tree shaped like discovery, `api/json?tree=jobs[url,_class,nextBuildNumber,inQueue,jobs[…]]`, reads no build records and costs about 240 bytes a job [live]. `nextBuildNumber` rises with every new build, even when old builds are discarded.
 * `queue/api/json?tree=items[id,inQueueSince,task[url]]` lists every queued item in one request [live].


### Batching

One tree with `builds[…]{0,5}` at every folder level returns everything in one request: 165 jobs came to 200 KB [live]. It costs the controller the sum of every job's query.


### Quirks

 * `builds[…]{0,5}` still walks up to 100 builds per job to count them, and can load build records from disk [source].
 * `estimatedDuration` on each build walks up to six earlier builds, so ask for it once per job through `lastBuild` [source].
 * Most `actions` entries in a build are empty: 5,239 of 5,398 [live].
 * A controller behind Tomcat rejects `{` and `}` left unencoded in the query with a 400; unencoded `[` and `]` are accepted [live].
 * Anonymous `api/json` on ci.jenkins.io returns a static `{}`.
 * Disabled and never built branch jobs are still listed.


### Sources

 * [Remote access API](https://www.jenkins.io/doc/book/using/remote-access-api/)
 * Source: [jenkinsci/jenkins](https://github.com/jenkinsci/jenkins) (`Api.java`, `Job.java`, `Run.java`, `RunList.java`, `Api/index.jelly`) and [jenkinsci/stapler](https://github.com/jenkinsci/stapler) (`ResponseImpl.java`, `Range.java`, `Property.java`)
