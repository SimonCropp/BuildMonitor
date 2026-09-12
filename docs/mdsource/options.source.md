# Options

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Options.verified.png">


## Run at startup

Starts BuildMonitor at login. On Windows this is a value under the current user's Run registry key; on macOS a launch agent in `~/Library/LaunchAgents`; on Linux a desktop entry in `~/.config/autostart`. Each points at the `buildmonitor` command in `~/.dotnet/tools`, which stays put across updates.


## Show the window at startup

Whether the window opens when the tray starts, or only the icon appears.


## Show running builds on other branches

A pipeline's row is its latest run on any branch. With this on, every other branch that is queued or running right now gets a row too.


## Notify when a build fails

Pops a desktop notification when a poll finds a build that has failed since the previous poll. The first poll after starting is silent, so a red pipeline that has been red for a week is not announced every login. On Windows this is a balloon from the tray icon, on macOS a Notification Center banner, on Linux whatever `notify-send` reaches.


## Poll intervals

How often each connection is polled, in seconds, and the shorter interval used while one of its builds is running. GitHub answers an unchanged repository from its cache without counting it against the rate limit, so a short interval is cheap there; Bitbucket allows a thousand requests an hour, so it is polled no more than once a minute whatever is set here.


## Local port

The loopback port the tray listens on. The `buildmonitor` command and the [MCP server](mcp.md) use it to reach the running tray, and it is what stops a second tray starting. Change it when something else already uses 3796. Takes effect after a restart, and can be overridden for one run with the `BuildMonitor_Port` environment variable.


## Connections

The CI services being watched. Click one to edit it, or Add connection for a new one. See [Authentication](auth.md) for what each provider needs.

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.ConnectionNew.verified.png">

Test checks the credential without saving. Sign in starts a browser or device sign in where the provider supports one. Save stores the credential in the platform's secret store and starts polling; nothing secret is written to settings.json.


## Update

Runs `dotnet tool update` for BuildMonitor and restarts it. On Windows the update runs after the tray has exited, because a running executable cannot be replaced.


## Open logs

Opens the log directory, which sits beside the installed tool. See [Troubleshooting](troubleshooting.md).


## Raise issue

Opens a new GitHub issue with the version, operating system and log location filled in.
