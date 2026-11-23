# Options

Windows:

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Options.verified.png">

macOS:

<img src="../src/BuildMonitor.Tests/Native/PixelTests.Options.OSX.verified.png">

Linux:

<img src="../src/BuildMonitor.Tests/Native/PixelTests.Options.Linux.verified.png">

Nothing here takes effect until Save; Cancel, or Escape, leaves everything as it was. The CI services being watched are on their own page, [Connections](connections.md).


## General


### Run at startup

Starts BuildMonitor at login. On Windows this is a value under the current user's Run registry key; on macOS a launch agent in `~/Library/LaunchAgents`; on Linux a desktop entry in `~/.config/autostart`. Each points at the `buildmonitor` command in `~/.dotnet/tools`, which stays put across updates.


### Show the window at startup

Whether the window opens when the tray starts, or only the icon appears.


### Notify when a build fails

Pops a desktop notification when a poll finds a build that has failed since the previous poll. The first poll after starting is silent, so a red pipeline that has been red for a week is not announced every login. On Windows this is a balloon from the tray icon, on macOS a Notification Center banner, on Linux whatever `notify-send` reaches.


### Theme

System takes the desktop's own light or dark setting. Dark and Light keep to one whatever the desktop uses.


## Builds


### Show other branches that are running or failing

A pipeline's row is its own latest build, on its default branch. With this on, each other branch whose latest build is running, queued or failed, a pull request's among them, gets a row of its own, and hovering the pipeline's row lists the branches whose latest build passed. Off, a pipeline is its one row. See [The window](tray.md#the-window).


### Show forks and collaborator repositories

Off by default. GitHub then watches the repositories owned by the signed in account and by the organizations it belongs to, and leaves out forks and repositories where the account is only a collaborator. They are left out of discovery itself, so they cost no API calls. Turning it on discovers them on the next poll.


### Show builds from the last (days)

How far back a finished build is shown, 30 days unless changed, from 1 to 365. A pipeline whose last run is older has no row. Running and queued builds always show, however long ago they were queued, and a build that started before the limit shows once it finishes inside it.

GitLab CI takes the limit as part of the request (`updatedAfter`, or `updated_after` over REST), so a poll of a busy account returns less. The date sent is the start of a UTC day, so the request's URL stays the same all day and a cheap "not modified" answer keeps working.

Every other service's older builds are dropped as they arrive. GitHub Actions, Azure DevOps and TeamCity can filter by date, but only on when a build was created or queued, which would also leave out a build from before the limit that is still running or was re-run.


### Group passing builds by prefix

Project name prefixes, comma separated, that group passing builds ahead of the repository name. A family of repositories named alike — `TheProjectApi`, `TheProjectUI`, `TheProjectManager`, `TheProjectDataModel` — otherwise takes a group each, and a dozen green rows bury the ones that need reading as surely as one repository's workflows do. Typing `TheProject` makes them one group named `TheProject`.

Matched against the name the row's first column shows, ignoring case, so the owner is no part of it. Where two prefixes both match, the longer one wins, which is how `TheProject` and `TheProjectManager` can both be listed. A build matching none is grouped by whatever the service files it under — an [Octopus project group](providers/octopus.md#rows) — and otherwise by its repository as before. Nothing but a passing build is grouped, whichever of the three named it.

A member of such a group names its own repository, since the group's row no longer does. A repository's pipelines follow each other in the group, and only the first names it.

Prefixes are added from here or from the [row's own menu](tray.md#the-window), which offers the ones its project shares with another and takes one back off from the group it made.


### Recent passes kept out of groups

How many of the most recent passing builds keep a row of their own rather than joining their group, 5 unless changed, from 0 to 50. Without it a build that has gone green folds into its group at once, and nothing on the screen says what has passed since. The newest are those queued most recently, which is the order the passing rows already sit in, so these are the first green rows. Older passes group as before, and a group left with one member is a plain row. With a filter typed, the count is taken from the builds that match. 0 groups every pass.


## Polling


### Poll intervals

How often a repository, project or pipeline that built recently is polled, in seconds, and the shorter interval used while a running build is expected to finish.

Each repository, project or pipeline is scheduled on its own. One that has not built for a while is polled less often, at about a thirtieth of the time since its last build: an hour after a success it is checked every two minutes, and never less often than every five minutes. After a failure it slows four times more gradually, because a fix or a retry is likely soon. A retry or cancel from the tray checks that pipeline again at once, and Refresh checks everything.

A running build gets the shorter interval from the fastest of its pipeline's last ten successful runs until a minute and a half past the slowest, or near the end of the provider's own estimate where it gives one, and is checked the moment that stretch begins rather than at its next poll. A build still running after that slows down, at about a tenth of how far it has overrun, since its estimate was plainly wrong, until it is polled like a quiet one.

A repository that keeps failing backs off on its own without holding up the rest. When a provider's rate limit runs low, every interval stretches until the limit recovers. Every provider but GitHub Actions, GitLab CI and Octopus Deploy lets a quiet repository, project or pipeline wait up to thirty minutes, because a cheaper request in between notices a new build sooner.

Polling waits while a row's right click menu is open, or the pointer is on one of a row's buttons, because a poll re-sorts the rows and would move what is being read, or aimed at, out from under the pointer. It carries on as soon as the menu closes or the button is clicked, and after half a minute whatever is still there.

While the computer is locked, nothing is polled more often than every five minutes, since nobody is watching a build finish, so an agent asking through the MCP server still gets builds about five minutes old at most. Failures are not announced while locked: unlocking shows one notification for what is failing then that was not when it locked, and refreshes everything. Waking from sleep refreshes everything too. On Linux this needs a desktop that tells logind the session is locked, as GNOME and KDE do.


## Local


### Code directory

Where the checkouts live. Browse picks it, or a path can be typed. Every git repository up to two folders below it, so both `code/DiffEngine` and `code/VerifyTests/DiffEngine`, gets a folder button on the rows of the pipelines it builds, which opens it in the file manager.

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.LocalRepos.verified.png">

A checkout is matched to a pipeline by its `origin` remote first: `git@github.com:VerifyTests/DiffEngine.git` matches the repository GitHub Actions, Travis CI, Bitbucket Pipelines and AppVeyor name, and GitLab CI's namespaced path. Where that does not match, the folder's own name does, which is all there is to go on for Azure DevOps, TeamCity, Octopus Deploy, GoCd and Jenkins, none of which report a repository. A folder never wins a row from a checkout whose remote matched it.

Scanning stops at a checkout rather than going through it, so a submodule or a vendored dependency is not listed as a repository of its own. Nothing in the checkout is read but `.git/config`, and nothing is written.

The tray menu gains an Open code directory item while this is set, above Open logs, which opens the folder itself rather than any one checkout.

The list is kept current while BuildMonitor runs: a repository cloned into the directory gets its button without a restart. Only the folders that could hold a checkout are watched, not everything below them, so a directory full of repositories and their build output costs a bounded number of watches. An empty field watches nothing.

Where [GitHub Desktop](https://desktop.github.com) is installed, a row whose repository is on GitHub, github.com or an enterprise server, and has no checkout here gets a clone button in place of the folder one, and its menu a Clone in GitHub Desktop item. Either opens GitHub Desktop's clone dialog with the repository filled in, which is where the folder it is cloned into is picked; a clone into the code directory turns the button into the folder one. No row offers a clone while this field is empty, since nothing then says which repositories are already checked out. Whether GitHub Desktop is installed is checked once, when BuildMonitor starts, so an install while it runs shows the button from the next start. GitHub Desktop has no Linux build, so there the button never shows.


### Triage prompt

Where a row's [Triage](mcp.md#triaging-failures) button sends the prompt it builds. It is always copied to the clipboard; this picks an AI app to open it in as well, with the prompt typed and waiting for Enter:

 * **First AI app found**, the default: the first of the apps below that is installed, looked for when the triage finishes, so an app installed later is picked up. With none, the prompt is only copied.
 * **Clipboard only**: never opens an app.
 * **Claude**: the [Claude desktop app](https://claude.com/download), which starts a new Claude Code session in the checkout.
 * **Claude Code in a terminal**: [Claude Code](https://code.claude.com/docs/en/deep-links) in a new terminal window, in the checkout. Claude Code registers its link the first time something is sent in an interactive session, so it is not found before then.
 * **Cursor**: Cursor's chat. Its link has no way to name a folder, so it opens in whatever Cursor had open, and the prompt names the checkout.

Only the apps found are listed, plus the one chosen if it has since gone; a chosen app that is not found leaves the prompt on the clipboard rather than opening another. An app is found by asking the desktop which app opens its links (`claude://`, `claude-cli://` and `cursor://`): the Windows file associations, Launch Services on macOS, `xdg-mime` on Linux.

A prompt is never cut short to fit. The Claude app keeps up to 14,336 characters of it, Claude Code 5,000, and Cursor takes links up to 8,000 characters once escaped; a longer one is only copied, and the status line says so. Hovering the Triage button says which app it opens.

None of the links can choose a model or an effort level: each carries only the prompt, and for the two Claude apps the checkout, so the session starts on whatever model and effort the app is already set to use. For Claude Code that is the `model` and `effortLevel` of its [settings](https://code.claude.com/docs/en/settings-reference), which apply to every session they reach, not only a triage's. Its `--model` and `--effort` options would set them for one session, but only when `claude` is started directly, and a prompt given on the command line is sent the moment it starts, before anyone has read what it says.


### Local port

The loopback port the tray listens on. The `buildmonitor` command and the [MCP server](mcp.md) use it to reach the running tray, and it is what stops a second tray starting. Change it when something else already uses 3796. Takes effect after a restart, and can be overridden for one run with the `BuildMonitor_Port` environment variable.


## About

About, beside Save and Cancel, opens a page with the version, a link to this documentation, and the three below, each of which acts the moment it is clicked rather than waiting for a Save. Back returns to the options as they were left, unsaved edits and all, and calling off an update started there comes back to About. Open logs, Raise issue and Update are in the tray menu too.


### Update

Opens a page saying what the update is about to do, and updates only once that is confirmed. BuildMonitor closes, runs `dotnet tool update` and starts again, and nothing is on screen in between: on Windows the update has to run after the tray has exited, because a running executable cannot be replaced.

For the same reason it first stops any [MCP server](mcp.md) BuildMonitor is running for an AI assistant, since each holds the installed version's files. The page lists them before anything is stopped, with how long each has been up, so an assistant in the middle of a conversation is not taken away unannounced; Cancel leaves them alone. The assistant sees its server stop, and connecting it again starts the new version; see [Updating](mcp.md#updating). On macOS and Linux a running file can be replaced, so the servers are left alone and the page says so.

BuildMonitor reports how the update went when it starts again. One that worked names the version now installed; one that failed gives the reason, and BuildMonitor is on the version it had. The whole output of `dotnet tool update` is in the [log](troubleshooting.md#logs). See [An update fails](troubleshooting.md#an-update-fails).


### Open logs

Opens the log directory, which sits beside the installed tool. See [Troubleshooting](troubleshooting.md).


### Raise issue

Opens a new GitHub issue with the version, operating system and log location filled in.
