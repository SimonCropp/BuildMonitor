# GitLab CI

Watches every project the user is a member of, or the projects of one group and its subgroups.


## Credential

A [personal access token](https://gitlab.com/-/user_settings/personal_access_tokens) with `api`, or `read_api` to only watch. Or sign in with PKCE or the device flow, once an application is registered; a self hosted GitLab needs its own application, whose id goes in the connection. See [Authentication](../auth.md).


## Rows

One per project. Merge request pipelines show the merge request number and link to it.


## Actions

 * Retry retries the failed jobs of the pipeline
 * Cancel cancels a pending or running pipeline


## Estimates

The countdown comes from the median of the project's last ten successful pipelines.
