# Troubleshooting


## Logs

The log directory sits beside the installed tool, so it moves with the version:

```
~/.dotnet/tools/.store/buildmonitor/{VERSION}/buildmonitor/{VERSION}/tools/net10.0/any/heads/{RID}/logs
```

The tray menu's "Open logs" and the Options page open it without any of that. Ten files of a megabyte each are kept.

The log holds information, warnings and errors. Set the `BuildMonitor_LogLevel` environment variable to `Debug` to also log each connection's schedule every poll, a line per repository or pipeline. On a large account that fills the ten files in about an hour, so set it only while chasing a problem.


## The tray is not running

`buildmonitor status` prints the connections and builds, or says nothing answered on the port. Run `buildmonitor` to start it. If it exits at once, the log says why; the usual reasons are settings.json not being readable, or the port being taken by something that is not BuildMonitor. Set the `BuildMonitor_Port` environment variable, or change the port in Options, to use another.


## A connection says "sign in required"

The credential was refused. Edit the connection and enter a new token, or sign in again. GitHub fine grained tokens and Azure DevOps personal access tokens also expire; check the date.


## A connection says "rate limited"

The provider asked for a pause. The row says when polling resumes, and Refresh waits for it, because retrying while limited risks the provider blocking the token. When the provider names no time, the pause starts at a minute and doubles while it keeps refusing, up to ten minutes.

A GitHub limit is shared by every tool signed in as the same account, and GitHub also limits bursts of requests a minute, so another tool can use up the quota. Lengthen the poll interval in Options if it keeps happening.


## A build on a quiet repository appears late

A repository, project or pipeline that has not built for a while is checked less often, up to every five minutes on GitHub Actions, GitLab CI and Octopus Deploy, and every thirty on the others, which look for new builds more cheaply in between. Refresh, or a retry from the tray, checks at once. See [Poll intervals](options.md#poll-intervals).


## Settings

`settings.json` in the local application data directory, under `BuildMonitor`: `%LocalAppData%` on Windows, `~/.local/share` on Linux and macOS. It holds no credentials. Set `BuildMonitor_Home` to run with a different directory.


## Reporting a problem

"Raise issue" in the tray menu or the Options page opens a new issue with the version, the operating system and the log location filled in. Attach the latest log file.
