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

Type in the Filter box at the top right to show only the builds whose repository, pipeline or branch, as the row names them, contain the text, ignoring case. A cross at the right of the box empties it, and shows only while there is something to empty; Escape does the same from anywhere in the window. A group keeps only the builds that match, so a filter reaches a build inside a closed group. The counts in the header and the tray icon still describe every build. The filter is not saved; to hide a pipeline for good, use [Filters](filters.md).

Two or more passing builds of one project share a group, closed, where the latest of them would have been: a repository with a handful of green workflows otherwise buries the rows that need reading. The group's row names the project and says how many builds it holds and how long since the latest. Click a group, press Enter on it or right click it to open or close it; open, each build is on a row beneath, indented under the group, with its project column left blank and its pipeline named, and right clicking one closes the group again. Projects are matched by repository name, so the same repository on two CI services is one group. Two things group ahead of that name: a prefix named in [Options](options.md), which groups a whole family of repositories, and the group the service itself files the pipeline under, which today is an Octopus project group. A member of either names its own repository rather than leaving the column blank, and opens it where the service reports one. Nothing else is grouped: a failed build keeps a row of its own that says which pipeline broke, and so does one running or queued.

Each row carries:

 * a status square; the squares of neighbouring rows touch, so a run of failures reads as one block
 * two marks: the logo of the service that ran the build, badged in its corner, and the bare logo of the service hosting its source. The badge says what that mark opens: a play for the run, a red cross where it broke, and a grey clock where the mark opens the pipeline's own page, which is a list of past runs and no run at all. The badge is also what tells the two marks apart where they are the same company, GitHub Actions being the octocat badged and the repository beside it the octocat alone. A host nothing here has a mark for, most self hosted Git, gets none, and nor does a provider that reports no repository
 * the repository, then the pipeline and branch; the pipeline is left out when it is named after the repository, as an AppVeyor project is. A Dependabot branch is the same 🤖 and the package it updates, so `dependabot/nuget/src/Foo-1.0` reads `🤖 Foo-1.0`
 * a progress bar and a countdown while the build runs, from the provider's own estimate where it gives one and otherwise from the median of the pipeline's last ten successful runs. A build that runs past its estimate shows how far over it is. Without any estimate the elapsed time is shown
 * who broke it, on a failed run only: their first name, or their whole name where two people on screen share one. An app is a 🤖 rather than a login, since which app it was says nothing the mark does not; two apps at once keep their names behind it
 * a pull request button, the number beside its mark, which opens the pull request
 * a retry button, for a failed or cancelled run; a cancel button, for a queued or running one. Neither shows where the provider reports that the credential or its user may not do it
 * a log button, for a failed run, which fetches the log of what failed and puts it on the clipboard. The status line says when it has arrived

A row that broke, or is still running or queued, leads with its run: it is the reason the row is being read, so the first cell and the mark before it open the run, and the mark of the source's host moves to the second cell. A settled row leads with its project instead, since its run is of no interest.

Either way the row shows both marks, and each part of it opens one thing, and only that thing:

 * the status square opens the run. It is on every build row, including one whose pipeline is left out and one inside a group, so the run is always a click away
 * the first cell, and the mark before it, open the run on a row that wants reading, and the repository on a settled one, on GitHub, GitLab, Bitbucket or wherever the source is. Jenkins, TeamCity and Octopus report no repository, so a settled row of theirs has a name in plain text rather than a link somewhere else
 * the mark leading the second cell opens whichever of the two the first cell did not
 * the pipeline opens the run on a settled row and its own page on the service — the AppVeyor project, the Jenkins job, the Actions workflow — on a row that leads with its run
 * the branch opens the branch
 * a group's row names the repository its builds share and opens it. Its members name their pipeline, since their own repository column is blank. A group its members do not share a repository with names none, so it opens nothing and each member names and opens its own instead

Hover any of them and it says where it goes, after about a second, written as what it opens and then which one: `Open branch: main`. Hover the rest of a row and it says what the row could not fit: the whole repository name and branch, the commit and who wrote it, and how long ago it started. The timing says where its estimate came from, and each button says what it does.

The buttons are marks rather than words, so a row carries all of them in the width one label used to take. The drop down names each of them in full, and so does the hover.

When the window is too narrow for a row's buttons, the repository, pipeline and branch keep their width and the buttons that do not fit go behind a … button at the end of the row, which lists them in a drop down. A pipeline and branch longer than about forty characters are cut short before that happens.

Right click a row for the same actions plus copying the build URL and excluding the pipeline, which adds an exact match to the [filters](filters.md).

The same menu offers to group by prefix, which is the inverse of excluding: the prefixes the row's project shares with another, longest first, each of which is added to the [group prefixes](options.md#group-passing-builds-by-prefix) and kept. `Utilities.Logging.Client` beside `Utilities.Logging.Server` and `Utilities.Storage` offers `Utilities.Logging` and then `Utilities`; a name with no separator is broken at its capitals, so `TheProjectApi` beside `TheProjectUi` offers `TheProject`. Only prefixes another project on screen shares are offered, and only where the shorter one reaches further than the last: neither a group of one nor the same group under a worse name is worth an item. A group's own row offers them as well, since a repository's group of green workflows is still a member of whatever family that repository belongs to, and the group a prefix made offers to stop grouping by it, which is where the grouping is seen rather than on the options page.

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
