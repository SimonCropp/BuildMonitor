# The tray and the window

BuildMonitor runs in the system tray. The icon shows the overall state:

 * grey: nothing is being watched, or everything is idle
 * blue: a build is queued or running
 * green: every latest build succeeded
 * red: a latest build failed
 * amber: a connection needs attention, because its credential was refused or polling failed

Left click the icon to open the window. Right click it for the menu: every failing or running build, repository first, with its own open, retry and cancel entries, then Open, Refresh, Options, Filters, Open logs, Raise issue, Update and Exit. When the connections span more than one CI service, each build shows its provider's logo.


## The window

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Builds.verified.png">

One list of pipelines across every connection, showing the latest run on any branch. A branch with a queued or running build gets a row of its own beside the latest one, which is what makes a pull request build visible while it runs; turn that off in [Options](options.md).

Rows sort what is happening now to the top: running, then queued, then failed, then everything else by age.

Type in the Filter box at the top right to show only the builds whose repository, pipeline or branch contain the text, ignoring case. A group keeps only the builds that match, so a filter reaches a build inside a closed group. The counts in the header and the tray icon still describe every build. The filter is not saved; to hide a pipeline for good, use [Filters](filters.md).

Two or more failed builds of one project share a group, and so do two or more that passed; a group never mixes the two. The group's row names the project and says how many builds it holds and how long since the latest. Failed groups start open, with each build on a row beneath, its project column left blank; passed groups start closed. Click a group, press Enter on it or right click it to open or close it; right click a build inside it to close it again. Projects are matched by repository name, so the same repository on two CI services is one group.

Each row carries:

 * a status square; the squares of neighbouring rows touch, so a run of failures reads as one block
 * the provider's logo, when the connections span more than one CI service
 * the repository, then the pipeline and branch; the pipeline is left out when it is named after the repository, as an AppVeyor project is
 * a progress bar and a countdown while the build runs, from the provider's own estimate where it gives one and otherwise from the median of the pipeline's last ten successful runs. A build that runs past its estimate shows how far over it is. Without any estimate the elapsed time is shown
 * links in the text: the pipeline opens the run and the branch opens the branch in the repository. Where the pipeline is left out, the repository opens the run instead
 * PR, which opens the pull request
 * Retry, for a failed or cancelled run; Cancel, for a queued or running one
 * Copy log, for a failed run, which fetches the log of what failed and puts it on the clipboard. The status line says when it has arrived

When the window is too narrow for a row's buttons, the repository, pipeline and branch keep their width and the buttons that do not fit go behind a … button at the end of the row, which lists them in a drop down. A pipeline and branch longer than about forty characters are cut short before that happens.

Right click a row for the same actions plus copying the build URL and excluding the pipeline, which adds an exact match to the [filters](filters.md).

A connection that needs signing in again, is rate limited or failing to poll says so at the bottom right of the window, and the tray icon turns amber.

Closing the window hides it; the tray keeps running. Exit is in the tray menu.


## Keyboard

 * Up and Down move the selection, PageUp, PageDown, Home and End scroll
 * Enter opens the selected build, or expands a shared green row, R retries it, Ctrl+C copies its URL
 * Ctrl+F (Cmd+F on macOS) moves to the Filter box. Up, Down, PageUp, PageDown and Enter still work from there, and Escape empties the box before it hides the window
 * F5 refreshes
 * Escape hides the window, or cancels a form
 * Ctrl+Q exits


## Command line

The `buildmonitor` command starts the tray, or shows the window of the one already running. It also takes `show`, `hide`, `quit`, `refresh`, `status` (prints the connections and builds) and `mcp` (see [MCP](mcp.md)).
