# The tray and the window

BuildMonitor runs in the system tray. The icon shows the overall state:

 * grey: nothing is being watched, or everything is idle
 * blue: a build is queued or running
 * green: every latest build succeeded
 * red: a latest build failed
 * amber: a connection needs attention, because its credential was refused or polling failed

Left click the icon to open the window. Right click it for the menu: Open, Refresh, Options, Filters, Open logs, Raise issue, Update and Exit. On macOS either click opens the menu, and Open shows the window. Open code directory sits above Open logs once a [code directory](options.md#code-directory) is set, and opens that folder; it is left out entirely while the option is empty.

On Windows 11 the icon starts on the taskbar rather than behind the arrow with the hidden icons. Windows remembers where an icon goes for each program path, and every update runs BuildMonitor from a new one, so each version takes the placement of the version before it. Hide the icon under Settings, Personalization, Taskbar, Other system tray icons, and it stays hidden after updates too.


## The window

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Builds.verified.png">

One list of pipelines across every connection, showing the latest run on any branch. A branch with a queued or running build gets a row of its own beside the latest one, which is what makes a pull request build visible while it runs; turn that off in [Options](options.md).

Rows sort what is happening now to the top: running, then queued, then failed, then everything else by age.

Type in the Filter box at the top right to show only the builds whose repository, pipeline or branch, as the row names them, contain the text, ignoring case. A group keeps only the builds that match, so a filter reaches a build inside a closed group. The counts in the header and the tray icon still describe every build. The filter is not saved; to hide a pipeline for good, use [Filters](filters.md).

Two or more failed builds of one project share a group, and so do two or more that passed; a group never mixes the two. The group's row names the project and says how many builds it holds and how long since the latest. Failed groups start open, with each build on a row beneath, its project column left blank and its pipeline named; passed groups start closed. Click a group, press Enter on it or right click it to open or close it; right click a build inside it to close it again. Projects are matched by repository name, so the same repository on two CI services is one group.

Each row carries:

 * a status square; the squares of neighbouring rows touch, so a run of failures reads as one block
 * the provider's logo
 * the repository, then the pipeline and branch; the pipeline is left out when it is named after the repository, as an AppVeyor project is. A Dependabot branch is the same 🤖 and the package it updates, so `dependabot/nuget/src/Foo-1.0` reads `🤖 Foo-1.0`
 * a progress bar and a countdown while the build runs, from the provider's own estimate where it gives one and otherwise from the median of the pipeline's last ten successful runs. A build that runs past its estimate shows how far over it is. Without any estimate the elapsed time is shown
 * who broke it, on a failed run only: their first name, or their whole name where two people on screen share one. An app is a 🤖 rather than a login, since which app it was says nothing the mark does not; two apps at once keep their names behind it
 * a pull request button, the number beside its mark, which opens the pull request
 * a retry button, for a failed or cancelled run; Cancel, in words, for a queued or running one. Neither shows where the provider reports that the credential or its user may not do it
 * a log button, for a failed run, which fetches the log of what failed and puts it on the clipboard. The status line says when it has arrived

Each part of a row opens one thing, and only that thing:

 * the status square opens the run. It is on every build row, including one whose pipeline is left out and one inside a group, so the run is always a click away
 * the repository opens the repository, on GitHub, GitLab, Bitbucket or wherever the build came from. Jenkins, TeamCity and Octopus do not report a repository, so on their rows the name is plain text rather than a link somewhere else
 * the provider's logo opens the pipeline's own page on that service: the AppVeyor project, the Jenkins job, the Actions workflow
 * the pipeline opens the run, and the branch opens the branch
 * a group's row names the repository its builds share and opens it. Its members name their pipeline, since their own repository column is blank

Hover any of them and it says where it goes, after about a second. Hover the rest of a row and it says what the row could not fit: the whole repository name and branch, the commit and who wrote it, and how long ago it started. The timing says where its estimate came from, and each button says what it does.

The buttons are marks rather than words, so a row carries all of them in the width one label used to take; Cancel keeps its word, since it is the one that stops something already running and the rows that carry it carry nothing else. The drop down names each of them in full, and so does the hover.

When the window is too narrow for a row's buttons, the repository, pipeline and branch keep their width and the buttons that do not fit go behind a … button at the end of the row, which lists them in a drop down. A pipeline and branch longer than about forty characters are cut short before that happens.

Right click a row for the same actions plus copying the build URL and excluding the pipeline, which adds an exact match to the [filters](filters.md).

A connection that needs signing in again, is rate limited or failing to poll says so at the bottom right of the window, and the tray icon turns amber.

Closing the window hides it; the tray keeps running. Exit is in the tray menu.


## Keyboard

 * Up and Down move the selection, PageUp, PageDown, Home and End scroll
 * Enter opens the selected build, or opens or closes the selected group, R retries the selected build, Ctrl+C copies its URL
 * Ctrl+F (Cmd+F on macOS) moves to the Filter box. Up, Down, PageUp, PageDown and Enter still work from there, and Escape empties the box before it hides the window
 * F5 refreshes
 * Escape hides the window, or cancels a form
 * Ctrl+Q exits


## Command line

The `buildmonitor` command starts the tray, or shows the window of the one already running. It also takes `show`, `hide`, `quit`, `refresh`, `status` (prints the connections and builds) and `mcp` (see [MCP](mcp.md)).
