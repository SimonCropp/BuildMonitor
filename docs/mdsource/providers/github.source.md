# GitHub Actions

Watches the workflows of every repository the token can see, or of one organization or user when the connection names one. Repositories with no push in ninety days, archived ones and disabled ones are skipped.


## Credential

A fine grained [personal access token](https://github.com/settings/personal-access-tokens/new) with Actions read and write and Metadata read on the repositories to watch, or a classic token with the `repo` scope. Or sign in: the device flow, or the browser flow once an OAuth App is registered. See [Authentication](../auth.md).

For GitHub Enterprise Server enter the server URL; the API is reached under `/api/v3`.


## Rows

One per workflow, showing the latest run. A run on another branch that is queued or running gets its own row. Pull request runs link to the pull request.


## Actions

 * Retry re-runs only the failed jobs of a failed run, and the whole run otherwise
 * Cancel cancels a queued or running run


## Estimates

GitHub gives none. The countdown comes from the median of the workflow's last ten successful runs.


## Polling

Runs are fetched per repository with a conditional request. GitHub answers an unchanged repository with a 304 that does not count against the five thousand requests an hour, so a short interval is affordable.
