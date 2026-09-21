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

Claude Desktop's Code tab runs Claude Code, so the `claude mcp add` above already covers it. Its chats read a registry of their own, `claude_desktop_config.json`: under `%APPDATA%\Claude` on Windows, and `~/Library/Application Support/Claude` on macOS.

Where Claude Desktop is installed as a Windows app package, `%APPDATA%` is virtualized, and that path is one only the app itself resolves. An editor started outside the package finds no `Claude` folder in `%APPDATA%` at all, and has to be pointed at the physical location instead: `%LocalAppData%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude`, with `AnthropicPBC.Claude_fnn82j28hfe8t` as the package folder for some builds.

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

Quit Claude Desktop before editing that file, and start it again after saving it. Closing the window is not enough, since that leaves it running. While it runs it saves its own settings to the same file, such as which Code session was last open, and it writes the file from what it read at startup, so an edit made in the meantime is lost. It reads the file only at startup.

The chats start the server with the desktop session's environment rather than a shell's, so where `buildmonitor` resolves only because a shell profile adds it to the path, give the absolute path to the tool shim instead: `.dotnet\tools` under the user profile on Windows, and `.dotnet/tools` under the home directory on macOS. Write that path out in full, since the server is started without a shell and so nothing expands `%USERPROFILE%` or `~`, and double the backslashes that JSON reads as escapes: `"command": "C:\\Users\\<user>\\.dotnet\\tools\\buildmonitor.exe"`, or `"command": "/Users/<user>/.dotnet/tools/buildmonitor"`.


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

The tools are for the assistant to call, not commands to type: `/list_failing` is an unknown command. Ask for what is wanted, such as "what's failing?" or "show me the log of the failed build", and the assistant picks the tool. The one command the server adds is [triage](#triaging-failures).

The assistant picks from each tool's description, and from instructions the server sends when it connects: that BuildMonitor watches the user's CI builds and deployments, so a question about builds means these even when it does not name BuildMonitor. Inside a repository, where "what's building?" could also mean a local build, naming CI or BuildMonitor in the question removes the doubt.

Every build carries a `key`, which is what the acting tools take, so an assistant asked to act on a build usually lists the builds first to find it.

A build or pipeline whose repository is checked out under the [code directory](options.md#code-directory) also carries a `directory`, the path it is checked out at, so the code behind a failure can be opened without asking where it lives. The field is absent rather than empty where no checkout was found, which is every build until that option is set.


### `list_builds`

The latest build of every pipeline, with status, timing, progress and links. An optional filter keeps the builds whose pipeline, repository, branch or connection contains it.

 * "What's building right now?"
 * "How long until the running builds finish?"
 * "Show me the latest builds on GitHub."


### `list_failing`

The pipelines whose latest build failed.

 * "What's failing?"
 * "Which failing builds do I have checked out locally?"


### `get_build`

One build by key: its status, commit, author and links, and whether it can be retried or cancelled.

 * "Who broke the build on main, and in which commit?"
 * "Can the failed release build be retried?"


### `list_runs`

The recent runs of one pipeline, newest first, where `list_builds` shows only the latest. Takes a build key or a pipeline key. Runs on one branch share the build key and differ by run number.

 * "Is this the first time the tests have failed, or have they been red for a while?"
 * "When did the build on main last pass?"


### `list_pipelines`

Every pipeline being monitored, including ones that have run nothing lately and so appear in no build list, each with a count of the runs held for it.

 * "Which pipelines haven't run lately?"
 * "Is the nightly pipeline being watched?"


### `get_build_log`

The log of a build, fetched from the CI service: each failed job, step or task under a line naming it, or the whole build's log where the service keeps one. Only the end of each section is returned, under a count of the lines dropped before it; `maxLines` sets how much, and defaults to 200.

 * "Why did the build fail?"
 * "Show me the last 50 lines of each failed job."
 * "Read the log of the failing build and fix the code that broke it."


### `download_build_artifacts`

Downloads a failed build's artifacts to a local directory and writes its whole log beside them as `log.txt`, then returns that directory and what is in it. Nothing comes back through the call itself: the assistant reads the files from disk with its own file tools.

 * "Download the artifacts of the failing build and read the test report."
 * "Get the crash dump from that run."

The files go under the [app's own directory](#where-the-files-go), not the operating system's temp directory, and are deleted after 24 hours.

A total of 50 MB is downloaded per build, at most 20 MB of any one file and at most 20 files, taking test reports, logs, approval output, screenshots and coverage before anything else, and smallest first within each of those. Anything left out is named with its size and the reason, so a report that was skipped is never mistaken for one that was never published. Jenkins reports no size for an artifact, so those are fetched under the per-file limit and stopped if they run past it.

Bitbucket and Travis have no artifact API: Bitbucket does not expose a pipeline's artifacts, and Travis stores none of its own. For a build on either, the call answers with the log alone and says which service it could not ask, rather than reporting that the run published nothing.


### `summary`

Counts of failing and running builds, the tray state, and each connection's health.

 * "How are my builds doing?"
 * "Is anything failing or still running?"


### `list_connections`

The connections and whether polling them works. Never returns credentials.

 * "Are all my CI connections working?"
 * "Why are no GitLab builds showing?"


### `refresh`

Polls now rather than waiting for the next interval, for every connection or only one. It returns before the poll has run, so the builds change a few seconds later. A running build is already polled often near when it should finish, so refreshing is not needed to wait for one. A refresh fetches every repository, project or pipeline of the connections it covers, where the schedule fetches only those that are due.

 * "Check the builds again."
 * "I've pushed a fix, refresh GitHub."


### `retry_build`

Re-runs a finished build; failed jobs only where the provider supports that.

 * "Retry the failed build."
 * "Re-run the failed jobs of the build on main."


### `cancel_build`

Cancels a queued or running build.

 * "Cancel the build running on my feature branch."
 * "Stop the queued nightly build."


### `run_build_next`

Moves a build still in the queue to the front of it, so it is the next one to start. Only some services can reorder a queue, and only a build still waiting can be moved; a build that can says so with `canRunNext`. See [Queue order](providers/api-comparison.md#queue-order).

 * "Push the queued release build to the front of the queue."
 * "Run the build on main next."


### `open_build_in_browser`

Opens the build, its branch or its pull request.

 * "Open the failing build in my browser."
 * "Open the pull request for that build."


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

The prompt does not download anything itself. It covers every failing build, so fetching artifacts up front would mean a download per row before the command answered, for builds the assistant then folds into one shared cause and never opens. It reads the logs first and calls [`download_build_artifacts`](#download_build_artifacts) for the builds whose logs point at a published file.

The tray offers the same thing for one row: a **Triage** button on a failed build whose repository is checked out. It downloads that build's artifacts and log, then copies a prompt naming the checkout and each downloaded file, to paste into any assistant. Nothing needs to be connected to BuildMonitor for that prompt to be useful, since everything it refers to is already on disk.

The download can take a while, and the prompt only reaches the clipboard once it is done, so pasting straight after the click pastes whatever was copied before. Until then the button shows an hourglass and the footer names the build being collected. Once the prompt is on the clipboard a notification says it is ready to paste, or says it could not be copied. That notification is shown whether or not [failures are announced](options.md#notify-when-a-build-fails).


### Where the files go

Downloads land beside the other per-user files, under `%LocalAppData%\BuildMonitor\artifacts` on Windows and `~/.local/share/BuildMonitor/artifacts` on macOS and Linux, or under `BuildMonitor_Home` where that is set. Each build gets its own directory, named after its repository and run.

They are kept for 24 hours. The sweep runs when the tray starts and before each triage, so a directory left by a triage is cleared by the next one a day later, or by the next start. Nothing else writes there, and deleting the folder by hand is safe.

A second triage of the same run replaces that run's directory rather than adding to it, so the file list always matches what the CI service has now. A retry is a different run and gets its own.


## Updating

Each assistant runs its own `buildmonitor mcp` process, which stays on the version it started with until the assistant connects it again. After an update, connect it again: from `/mcp` in the Claude Code CLI, which does not restart a local server by itself. In Claude Desktop, a new Code tab session starts a server of its own, and the chats need the app quit and started again.

On Windows a running server also holds the installed version's files, so `dotnet tool update` cannot remove them and fails with `Access to the path ... is denied`. The tray's [Update](options.md#update) stops the servers before it updates, and lists them first so it can be called off. From the command line, quit the tray with `buildmonitor quit` and close the assistants using the server first. See [An update fails](troubleshooting.md#an-update-fails).
