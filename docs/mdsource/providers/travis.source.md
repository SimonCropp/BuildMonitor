# Travis CI

Watches every active repository the token can see, on travis-ci.com or a Travis CI Enterprise server.


## Credential

An [API token](https://app.travis-ci.com/account/preferences), also shown by `travis token --com`.


## Rows

One per repository. Pull request builds link to the pull request on GitHub.


## Actions

 * Retry restarts the build
 * Cancel cancels a queued or running build


## Estimates

The countdown comes from the median of the repository's last ten successful builds.
