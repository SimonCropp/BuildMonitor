# BuildMonitor

A build/CI monitor that runs in the system tray on Windows, macOS and Linux. It polls the CI services a developer cares about, shows one row per pipeline with a progress bar and countdown for running builds, links straight to the build, the branch and the pull request, can retry or cancel a build, and copies the log of a failed one. A local MCP server lets an AI assistant read and act on the same list.

![The window](https://raw.githubusercontent.com/SimonCropp/BuildMonitor/main/src/BuildMonitor.Windows.Tests/MonitorFormTests.Searched.verified.png)


## Install

```
dotnet tool install --global BuildMonitor --prerelease
```

Run `buildmonitor` to start the tray app. One install works on every operating system: the package carries a head for each platform and the launcher starts the right one.

The command also takes `show`, `hide`, `quit`, `refresh`, `status` and `mcp`.


## Supported services

AppVeyor, Azure DevOps, Bitbucket Pipelines, GitHub Actions, GitLab CI, GoCD, Jenkins, Octopus Deploy, TeamCity and Travis CI.

Each takes a token or API key created in that service's own UI, and GitHub, GitLab and Azure DevOps can also sign in through the browser. Credentials are kept in the platform's secret store: the Windows Data Protection API, the macOS login Keychain, or the Secret Service on Linux. settings.json never holds one.


## MCP server

`buildmonitor mcp` runs a [Model Context Protocol](https://modelcontextprotocol.io/) server over stdio, so a local AI assistant can read the same builds the tray shows and act on them. For Claude Code:

```
claude mcp add --transport stdio buildmonitor --scope user -- buildmonitor mcp
```

See [the MCP docs](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/mcp.md) for the other clients and the tools it exposes.


## Documentation

 * [Tray](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/tray.md)
 * [Options](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/options.md)
 * [Filters](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/filters.md)
 * [Authentication](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/auth.md)
 * [MCP server](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/mcp.md)
 * [Linux](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/linux.md)
 * [macOS](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/macos.md)
 * [Troubleshooting](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/troubleshooting.md)
 * [Provider APIs](https://github.com/SimonCropp/BuildMonitor/blob/main/docs/providers/api-comparison.md)

**See [Milestones](https://github.com/SimonCropp/BuildMonitor/milestones?state=closed) for release notes.**
