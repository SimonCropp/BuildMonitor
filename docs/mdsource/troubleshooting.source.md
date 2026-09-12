# Troubleshooting


## Logs

The log directory sits beside the installed tool, so it moves with the version:

```
~/.dotnet/tools/.store/buildmonitor/{VERSION}/buildmonitor/{VERSION}/tools/net10.0/any/heads/{RID}/logs
```

The tray menu's "Open logs" and the Options page open it without any of that. Ten files of a megabyte each are kept.


## The tray is not running

`buildmonitor status` prints the connections and builds, or says nothing answered on the port. Run `buildmonitor` to start it. If it exits at once, the log says why; the usual reasons are settings.json not being readable, or the port being taken by something that is not BuildMonitor. Set the `BuildMonitor_Port` environment variable, or change the port in Options, to use another.


## A connection says "sign in required"

The credential was refused. Edit the connection and enter a new token, or sign in again. A GitHub fine grained token also expires; check its date.


## A connection says "rate limited"

The provider asked for a pause. The row says when polling resumes. Lengthen the poll interval in Options if it keeps happening.


## Settings

`settings.json` in the local application data directory, under `BuildMonitor`: `%LocalAppData%` on Windows, `~/.local/share` on Linux and macOS. It holds no credentials. Set `BuildMonitor_Home` to run with a different directory.


## Reporting a problem

"Raise issue" in the tray menu or the Options page opens a new issue with the version, the operating system and the log location filled in. Attach the latest log file.
