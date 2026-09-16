# MCP server

`buildmonitor mcp` runs a [Model Context Protocol](https://modelcontextprotocol.io/) server over stdio, so a local AI assistant can read the same builds the tray shows and act on them. It starts the tray if it is not running and talks to it over the local port.


## Registering it

Claude Code:

```
claude mcp add --transport stdio buildmonitor --scope user -- buildmonitor mcp
```

Or in a `.mcp.json` (Claude Code, project scope) or `.cursor/mcp.json` (Cursor):

```json
{
  "mcpServers": {
    "buildmonitor": {
      "command": "buildmonitor",
      "args": ["mcp"]
    }
  }
}
```

Claude Desktop keeps its own registry, separate from the one Claude Code reads, in `claude_desktop_config.json`: under `%APPDATA%\Claude` on Windows, and `~/Library/Application Support/Claude` on macOS. Settings > Developer > Edit Config opens it.

```json
{
  "mcpServers": {
    "buildmonitor": {
      "command": "buildmonitor",
      "args": ["mcp"]
    }
  }
}
```

That file is read only at startup, and closing the window leaves Claude Desktop running in the tray, so quit it and start it again rather than closing it. It also starts the server with the desktop session's environment rather than a shell's, so where `buildmonitor` resolves only because a shell profile adds it to the path, give the absolute path to the tool shim instead: `%USERPROFILE%\.dotnet\tools\buildmonitor.exe`, or `~/.dotnet/tools/buildmonitor`.


VS Code, in `.vscode/mcp.json`:

```json
{
  "servers": {
    "buildmonitor": {
      "type": "stdio",
      "command": "buildmonitor",
      "args": ["mcp"]
    }
  }
}
```


## Tools

| Tool | What it does |
|---|---|
| `list_builds` | The latest build of every pipeline, with status, timing, progress and links. Optional substring filter on pipeline, repository, branch or connection. |
| `list_failing` | The pipelines whose latest build failed. |
| `get_build` | One build by key. |
| `list_runs` | The recent runs of one pipeline, newest first, where `list_builds` shows only the latest. Takes a build key or a pipeline key. Runs on one branch share the build key and differ by run number. |
| `list_pipelines` | Every pipeline being monitored, including ones that have run nothing lately and so appear in no build list, each with a count of the runs held for it. |
| `get_build_log` | The log of a build, fetched from the CI service: each failed job, step or task under a line naming it, or the whole build's log where the service keeps one. Only the end of each section is returned, under a count of the lines dropped before it; `maxLines` sets how much, and defaults to 200. |
| `summary` | Counts of failing and running builds, the tray state, and each connection's health. |
| `list_connections` | The connections and whether polling them works. Never returns credentials. |
| `refresh` | Polls now. |
| `retry_build` | Re-runs a finished build; failed jobs only where the provider supports that. |
| `cancel_build` | Cancels a queued or running build. |
| `open_build_in_browser` | Opens the build, its branch or its pull request. |

Every build carries a `key`, which is what the acting tools take.

A build or pipeline whose repository is checked out under the [code directory](options.md#code-directory) also carries a `directory`, the path it is checked out at, so the code behind a failure can be opened without asking where it lives. The field is absent rather than empty where no checkout was found, which is every build until that option is set.


## Triaging failures

The server also ships a prompt, `triage`, for working through every failing build whose code is checked out locally. A prompt is a command the user runs rather than a tool the assistant chooses to call: Claude Code lists it as `/buildmonitor:triage (MCP)` and runs it as `/mcp__buildmonitor__triage` too, and other clients offer it in their own prompt menu.

It covers every configured connection, whatever the CI service. What narrows it is the [code directory](options.md#code-directory): a failing build whose repository has no checkout under it has no code here to work on, so it is named at the end rather than worked, which is also where a deployment that builds nothing ends up. Until that option is set every failure is in that list, and the prompt says so.

Each build comes with its branch, run, commit, checkout and key, gathered under any pipeline it shares with others. That grouping is a hint, not a finding. Several repositories red on one shared workflow are a single fix, so the assistant is told to read the logs and group the failures by what those say before investigating anything, rather than open one investigation per row.

The checkout is left as it was found. It is where the user works, and may be on another branch or hold uncommitted changes, so a build that ran on a different branch is reached through a `git worktree` rather than by switching, stashing or discarding anything.

| Argument | What it does |
|---|---|
| `filter` | Optional. The substring filter `list_builds` takes, on pipeline, repository, branch or connection. Empty or `*` covers every failing build. |
| `fix` | Optional, and on only for `true`, `yes`, `on` or `1`. Off, each failure is diagnosed and reported with the fix it needs, and no source file is changed. On, each is fixed where it was reproduced and its tests run, with the changes left uncommitted. Nothing is committed or pushed either way. |

Claude Code splits a command's arguments on whitespace and binds them in order, so the filter comes first and is a single word, and `*` holds its place when only `fix` is wanted:

```
/mcp__buildmonitor__triage
/mcp__buildmonitor__triage SdkCheck
/mcp__buildmonitor__triage SdkCheck true
/mcp__buildmonitor__triage * true
```
