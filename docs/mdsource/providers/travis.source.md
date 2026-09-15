# Travis CI

Watches every active repository the token can see, on travis-ci.com or a Travis CI Enterprise server.


## Credential

An [API token](https://app.travis-ci.com/account/preferences), also shown by `travis token --com`.


## Rows

One per repository. Pull request builds link to the pull request on GitHub.


## Actions

 * Retry restarts the build
 * Cancel cancels a queued or running build
 * Copy log copies the logs of the failed and errored jobs, leaving out jobs allowed to fail


## Estimates

The countdown comes from the median of the repository's last ten successful builds.


## Polling

Each repository is fetched on its own schedule (see [Poll intervals](../options.md#poll-intervals)). Travis sends no ETags, so every request returns a full response, and no rate limit headers, so a limit is only seen when a request is refused; the pause then starts at a minute and doubles. Nothing cheap says which repositories have new builds, so a build on a quiet repository shows when its schedule comes round: within five minutes.

```mermaid
---
config:
  flowchart:
    wrappingWidth: 400
---
flowchart TD
    wake(["Wake: something is due,<br/>or Refresh, Retry or Cancel"]) --> listed{"Listed repositories in<br/>the last 10 minutes?"}
    listed -- "no" --> discover["GET repos?repository.active=true,<br/>up to 100"]
    listed -- "yes" --> interval["Each repository is polled<br/>as its latest builds need"]
    discover --> interval
    interval --> finishing["Started, from its fastest<br/>recent build to 90 s past<br/>its slowest, or with no<br/>history: every 10 s"]
    interval --> running["Started for less than its<br/>fastest recent build, or<br/>created or queued: every<br/>30 s, and again when it<br/>reaches that"]
    interval --> overrun["Started over 90 s past<br/>its slowest recent build:<br/>the time beyond that ÷ 10,<br/>30 s to 5 minutes"]
    interval --> quiet["Quiet: the time since<br/>the last build ÷ 30,<br/>30 s to 5 minutes"]
    interval --> failed["Quiet after a failed or<br/>errored build: the time<br/>since it ÷ 120,<br/>30 s to 5 minutes"]
    finishing --> due{"Due?"}
    running --> due
    overrun --> due
    quiet --> due
    failed --> due
    due -- "no" --> sleep(["Sleep until a repository<br/>or the listing is due"])
    due -- "yes, most urgent first" --> fetch["GET repo/{slug}/builds,<br/>the last 5 with<br/>their commits"]
    fetch -- "200" --> rows["Update its rows"]
    fetch -- "429" --> pause["Pause the connection<br/>for Retry-After, or from<br/>a minute, doubling"]
    fetch -- "other failure" --> backoff["Back off that repository,<br/>doubling up to 10 minutes"]
    rows --> sleep
    pause --> sleep
    backoff --> sleep
```


## API notes

Researched 2026-09-14. [live] means checked with anonymous requests against api.travis-ci.com; [docs] and [source] name the evidence. See [Provider APIs](api-comparison.md) for every provider side by side.


### Rate limits

2,000 requests a minute per token, and 500 a minute per IP address without one [source]. Successful responses carry no rate limit headers [live], and a 429 carries `Retry-After` only if the server enabled it, which it does not by default [source].


### Conditional requests

None, although CORS exposes an `Etag` header [live]. A build with its commit is about 3.6 KB.


### Change detection

 * `repos?repository.active=true&sort_by=current_build:desc&include=repository.last_started_build` puts the repositories with the newest builds first. The sort key is undocumented but works [live, source].
 * `current_build` is deprecated in favour of `last_started_build`, and neither includes builds created but not yet started [docs, live].
 * `/builds` returns only builds started by the token's own user [source].


### Batching

None beyond one build per repository.


### Quirks

 * A request without a login answers 403 `login_required` [live].
 * A build has no `created_at`, and its commit has no author unless requested with `include=build.commit` [live].


### Sources

 * [builds](https://developer.travis-ci.com/resource/builds), [repository](https://developer.travis-ci.com/resource/repository) and [repositories](https://developer.travis-ci.com/resource/repositories)
 * Source: [attack.rb](https://github.com/travis-ci/travis-api/blob/master/lib/travis/api/attack.rb), [builds.rb](https://github.com/travis-ci/travis-api/blob/master/lib/travis/api/v3/queries/builds.rb), [repositories.rb](https://github.com/travis-ci/travis-api/blob/master/lib/travis/api/v3/queries/repositories.rb) and [rack-attack](https://github.com/rack/rack-attack)
