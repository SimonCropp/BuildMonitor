# Jenkins

Watches every job on the server, walking folders and multibranch projects. A branch of a multibranch project is a pipeline of its own, and a branch job named `PR-n` is shown as pull request n.


## Credential

An [API token](https://www.jenkins.io/doc/book/system-administration/authenticating-scripted-clients/), created at `https://<server>/me/security`, together with the Jenkins user name.


## Rows

One per job. A job with a queued build shows a queued row until it starts.


## Actions

 * Retry queues a new build of the job; Jenkins has no rerun. A parameterized job is built with its default parameters
 * Cancel stops a running build, or removes a queued one from the queue


## Estimates

Jenkins reports an estimated duration for every build, from its own history, and that drives the countdown.
