# CircleCI

Watches the projects the token's user follows, and those that built lately in the organizations the user is in, on circleci.com or a CircleCI server.


## Credential

A [personal API token](https://app.circleci.com/settings/user/tokens), from User Settings. The API refuses a project token. It is sent as the `Circle-Token` header.

The organization is optional and narrows the connection to one: its slug, such as `gh/VerifyTests`, or its name alone.


## Rows

One per project, showing its latest pipeline on the project's default branch. The logo opens the project's pipelines on CircleCI and the name opens the repository, at the address CircleCI gives for it. A project that is not a GitHub or Bitbucket one, as those of an organization on CircleCI's GitHub App or on GitLab are, gets a repository link from its pipelines rather than from the project.

A pipeline has no status of its own, so a row's is its workflows':

 * running while any workflow is, including one CircleCI calls failing, which has a failed job and others still going
 * failed once one has failed, errored or was unauthorized
 * cancelled where one was cancelled and none failed
 * passed otherwise

A workflow on hold is waiting for someone to approve it, and decides nothing while another workflow has an answer: a pipeline that built and now waits to be deployed has passed. A rerun adds a workflow to the pipeline it reruns, so only the newest workflow of each name counts. A pipeline with no workflows, which is what a configuration that filters them all out leaves, is not shown unless it failed to be set up, which is a failure.

A pull request build links to the pull request CircleCI names for it. A pull request from a fork is built as `pull/n` and names none, and on GitHub links to that pull request.

A failed branch is asked of a [GitHub connection](github.md#rows), where there is one, whether its pull request was merged or closed or the branch deleted, and loses its row if so. Without one it loses its row once the default branch has built since it failed.


## Actions

 * Retry reruns the failed workflows from their failed jobs, or every workflow from its start where none failed
 * Cancel cancels the workflows that are running or on hold
 * Log copies the output of the failed steps of the failed jobs
 * Artifacts are those of every job, up to ten jobs a pipeline. CircleCI lists no sizes

An artifact is downloaded from the address CircleCI lists for it, with the token, and only where that address is CircleCI's own: the API's host, or on circleci.com one under `circle-artifacts.com` or `circleci.com`.


## Estimates

The countdown comes from the median of the project's last ten successful pipelines.


## Polling

Each project is fetched on its own schedule (see [Poll intervals](../options.md#poll-intervals)): its newest pipelines in one request, then a request for the workflows of each of the five shown. Once a minute each organization's newest pipelines are listed, and a project with a new one is fetched at once rather than when its schedule comes round, which for a quiet project is up to thirty minutes. A rerun adds a workflow to a pipeline already listed, so one started outside BuildMonitor waits for the schedule.

A project whose last five pipelines hold none on its default branch, as a burst of pull requests leaves them, has the rest of that page of twenty looked through, and is then asked for its newest pipeline on that branch. A project with none since the [history cutoff](../options.md#show-builds-from-the-last-days) is not asked again for an hour.

Projects are listed every ten minutes: the followed ones from API v1.1, then each organization's recent pipelines for any project that list does not have, which is asked for its name and default branch once.


## API notes

Researched 2026-10-09 from CircleCI's documentation and its OpenAPI description [docs]. Nothing here was checked against a live account. See [Provider APIs](api-comparison.md) for every provider side by side.


### Rate limits

Unpublished. A throttled request answers 429, in most cases with `Retry-After` [docs]; the pause otherwise starts at a minute and doubles.


### Two API versions

API v2 has pipelines, workflows, jobs and artifacts, but lists no projects and nothing inside a job. So two things come from v1.1, which CircleCI still serves and says it will retire at a date not yet given [docs]:

 * `GET api/v1.1/projects`, the projects the user follows, each with its default branch and repository address. Without it, projects are found only by their recent pipelines.
 * `GET api/v1.1/project/{slug}/{job number}`, a job's steps, and `…/output/{step}/{index}?file=true`, one step's output as text. Each step also carries a signed `output_url` on a storage host, which is not used: every request carries the token, and it is not sent off CircleCI.


### Change detection

`GET api/v2/pipeline?org-slug={slug}` lists the newest pipelines of the projects the user follows in an organization [docs]. A pipeline's own `state` says only whether it was set up, so a workflow finishing or being rerun moves nothing there.


### Batching

None. A pipeline's status takes a request for its workflows, so a project's five pipelines cost six requests.


### Sources

 * [API v2](https://circleci.com/docs/api/v2/) and its [OpenAPI description](https://circleci.com/api/v2/openapi.json)
 * [API developer's guide](https://circleci.com/docs/guides/toolkit/api-developers-guide/)
 * [Retrieving job step output via the API](https://support.circleci.com/hc/en-us/articles/32887967598619-Retrieving-job-step-output-via-the-API)
