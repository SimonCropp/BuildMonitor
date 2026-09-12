# AppVeyor

Watches every project the token can see.


## Credential

An [API token](https://ci.appveyor.com/api-keys). A v2 token, which spans every account the user belongs to, also needs the account name in the connection.


## Rows

One per project. Pull request builds link to the pull request on GitHub.


## Actions

 * Retry re-runs the build
 * Cancel cancels a queued or running build


## Estimates

The countdown comes from the median of the project's last ten successful builds.
