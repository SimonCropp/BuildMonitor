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
