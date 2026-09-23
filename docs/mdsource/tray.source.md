# The tray and the window

BuildMonitor runs in the system tray. The icon shows the overall state:

 * grey: nothing is being watched, or everything is idle
 * blue: a build is queued or running, on any branch
 * green: every pipeline's own latest build succeeded
 * red: a pipeline's own latest build failed. A pull request failing leaves the icon as its pipeline's own build has it
 * amber: a connection needs attention, because its credential was refused or polling failed

Left click the icon to open the window. Right click it for the menu: Open, Refresh, Connections, Options, Filters, Open logs, Raise issue, Update and Exit. On macOS either click opens the menu, and Open shows the window. Open code directory sits above Open logs once a [code directory](options.md#code-directory) is set, and opens that folder; it is left out entirely while the option is empty.

On Windows 11 the icon starts on the taskbar rather than behind the arrow with the hidden icons. Windows remembers where an icon goes for each program path, and every update runs BuildMonitor from a new one, so each version takes the placement of the version before it. Hide the icon under Settings, Personalization, Taskbar, Other system tray icons, and it stays hidden after updates too.


## The window

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Builds.verified.png">

One list of pipelines across every connection. A pipeline's row is its own latest build: the one on its default branch where BuildMonitor knows which that is, which each [provider's page](readme.md#providers) says, and its latest on any branch where it does not. A pull request's build never takes that row, so a green one cannot hide a red main, nor a red one paint main red.

Each of the pipeline's other branches, a pull request's among them, gets a row of its own while its latest build is running, queued or failed, naming the repository, the pipeline and the branch as any row does. A branch whose latest build passed, or was cancelled, has no row; hover the pipeline's row to see those. Turn the rows for other branches off in [Options](options.md#show-other-branches-that-are-running-or-failing).

A failed branch keeps its row only while it can still be fixed. Once its pull request is merged or closed, or the branch is deleted, it has no row either, and the pipeline's hover says which. The CI services keep a build after its branch is gone and do not say it went, so this is asked of the service holding the repository, through a connection to it: a [GitHub](providers/github.md#rows) connection answers for every build of a repository on GitHub, whichever service ran it, so an AppVeyor or Travis CI project's pull requests are asked of it too, and a [GitLab](providers/gitlab.md#rows), [Bitbucket](providers/bitbucket.md#rows) or [Azure DevOps](providers/azure-devops.md#rows) connection for its own repositories. A question goes only to a connection whose own address the repository is under, on that connection's credential. Where no connection can ask, or its credential may not see the repository, a failed branch goes once the pipeline's default branch has built since it failed instead.

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Lanes.verified.png">

Rows sort what is happening now to the top: running, then queued, then failed, then everything else by age. Each row sorts by its own build, another branch's too, so a failed pull request sits with the failures and its pipeline's green main with the green rows at the bottom.

The header counts pipelines, how many of them are failing, and how many builds are running on any branch. A pipeline is failing when its own build failed: a pull request failing is red on its own row, and is announced, but counts for nothing in the header or the tray icon, where a contributor's broken fork would otherwise keep main looking red.

The window opens where it was last left, at the size it had, and on Windows and Linux maximized if it was. If no screen reaches that any more, such as after a monitor is unplugged, it opens centred. On a Wayland desktop the compositor may place the window itself and keep only its size.

Type in the Filter box at the top right to show only the builds whose repository, pipeline or branch, as the row names them, contain the text, ignoring case. A cross at the right of the box empties it, and shows only while there is something to empty; Escape does the same from anywhere in the window. A group keeps only the builds that match, so a filter reaches a build inside a closed group. The counts in the header and the tray icon still describe every build. The filter is not saved; to hide a pipeline for good, use [Filters](filters.md).

Two or more passing builds of one project share a group, closed, where the latest of them would have been: a repository with a handful of green workflows otherwise buries the rows that need reading. The group's row names the project and says how many builds it holds and how long since the latest. Click a group, press Enter on it or right click it to open or close it, which is kept across restarts; open, each build is on a row beneath, indented under the group, with its project column left blank and its pipeline named, and right clicking one closes the group again. Projects are matched by repository name, so the same repository on two CI services is one group. Two things group ahead of that name: a prefix named in [Options](options.md), which groups a whole family of repositories, and the group the service itself files the pipeline under, which today is an Octopus project group. A member of either names its own repository rather than leaving the column blank, and opens it where the service reports one. A repository's pipelines follow each other there, and only the first names it. Nothing else is grouped: a failed build keeps a row of its own that says which pipeline broke, and so does one running or queued, on any branch.

Each row carries:

 * a status square; the squares of neighbouring rows touch, so a run of failures reads as one block
 * two marks: the logo of the service that ran the build, badged in its corner, and the bare logo of the service hosting its source. The badge says what that mark opens: a play for the run, a red cross where it broke, and a grey clock where the mark opens the pipeline's own page, which is a list of past runs and no run at all. The badge is also what tells the two marks apart where they are the same company, GitHub Actions being the octocat badged and the repository beside it the octocat alone. A host nothing here has a mark for, most self hosted Git, gets none, and nor does a provider that reports no repository
 * the repository, then the pipeline and branch, the branch behind a branch mark so the two read apart even where either name has spaces in it; the pipeline is left out when it is named after the repository, as an AppVeyor project is. A Dependabot branch is the same 🤖 and the package it updates, so `dependabot/nuget/src/Foo-1.0` reads `🤖 Foo-1.0`
 * a progress bar and a countdown while the build runs, from the provider's own estimate where it gives one and otherwise from the median of the pipeline's last ten successful runs. A build that runs past its estimate shows how far over it is. Without any estimate the elapsed time is shown
 * who broke it, on a failed run only: their first name, or their whole name where two people on screen share one. An app is a 🤖 rather than a login, since which app it was says nothing the mark does not; two apps at once keep their names behind it
 * a pull request button, the number beside its mark, which opens the pull request
 * a retry button, for a failed or cancelled run; a cancel button, for a queued or running one. Neither shows where the provider reports that the credential or its user may not do it
 * a log button, for a failed run, which fetches the log of what failed and puts it on the clipboard. The status line says when it has arrived

A row for another branch carries what any row does, its pull request's button among them.

A row that broke, or is still running or queued, leads with its run: it is the reason the row is being read, so the first cell and the mark before it open the run, and the mark of the source's host moves to the second cell. A settled row leads with its project instead, since its run is of no interest.

Either way the row shows both marks, and each part of it opens one thing, and only that thing:

 * the status square opens the run. It is on every build row, including one whose pipeline is left out and one inside a group, so the run is always a click away
 * the first cell, and the mark before it, open the run on a row that wants reading, and the repository on a settled one, on GitHub, GitLab, Bitbucket or wherever the source is. Jenkins, TeamCity and Octopus report no repository, so a settled row of theirs has a name in plain text rather than a link somewhere else
 * the mark leading the second cell opens whichever of the two the first cell did not
 * the pipeline opens the run on a settled row and its own page on the service — the AppVeyor project, the Jenkins job, the Actions workflow — on a row that leads with its run
 * the branch, and the mark before it, open the branch
 * a group's row names the repository its builds share and opens it. Its members name their pipeline, since their own repository column is blank. A group its members do not share a repository with names none, so it opens nothing and each member names and opens its own instead

Hover any of them and it says where it goes, after about a second, written as what it opens and then which one: `Open branch: main`. Hover the rest of a row and it says what the row could not fit: the whole repository name and branch, the commit and who wrote it, and how long ago it started. A pipeline's row also lists its other branches that have no row of their own, each with its pull request, how its latest build ended and when, and for a failed one what became of it since, the first five and then how many more. The timing says where its estimate came from, and each button says what it does.

The buttons are marks rather than words, so a row carries all of them in the width one label used to take. The drop down names each of them in full, and so does the hover.

When the window is too narrow for a row's buttons, the repository, pipeline and branch keep their width and the buttons that do not fit go behind a … button at the end of the row, which lists them in a drop down. A pipeline and branch longer than about forty characters are cut short before that happens.

Right click a row for the same actions plus copying the build URL and excluding the pipeline, which adds an exact match to the [filters](filters.md). A group's row offers the excludes that all of its builds share, such as their repository.

The same menu offers to group by prefix, which is the inverse of excluding: the prefixes the row's project shares with another, longest first, each of which is added to the [group prefixes](options.md#group-passing-builds-by-prefix) and kept. `Utilities.Logging.Client` beside `Utilities.Logging.Server` and `Utilities.Storage` offers `Utilities.Logging` and then `Utilities`; a name with no separator is broken at its capitals, so `TheProjectApi` beside `TheProjectUi` offers `TheProject`. Only prefixes another project on screen shares are offered, and only where the shorter one reaches further than the last: neither a group of one nor the same group under a worse name is worth an item. A group's own row offers them as well, since a repository's group of green workflows is still a member of whatever family that repository belongs to, and the group a prefix made offers to stop grouping by it, which is where the grouping is seen rather than on the options page.

The buttons along the bottom are Refresh, [Connections](connections.md), [Options](options.md), [Filters](filters.md) and Hide. Until there is a connection, Add connection stands where Refresh and Connections would.

A connection that needs signing in again, is rate limited or failing to poll says so at the bottom right of the window, and the tray icon turns amber. One that needs signing in, or is failing, also gets a Sign in or Check connection button beside the others, which opens its editor.

Closing the window hides it; the tray keeps running. Exit is in the tray menu.


## Keyboard

On macOS, Cmd stands in for Ctrl, except for Retry.

| Key | Does |
|---|---|
| Up, Down | Move the selection |
| PageUp, PageDown, Home, End | Scroll |
| Enter | Open the selected build, or open or close the selected group |
| Ctrl+R (Shift+Cmd+R on macOS) | Retry the selected build |
| Ctrl+. | Cancel the selected build |
| Ctrl+L | Copy the selected failed build's log |
| Ctrl+T | Triage the selected failed build |
| Ctrl+C | Copy the selected build's URL |
| Shift+F10, or the Menu key | Open the selected row's menu |
| Ctrl+F | Move to the Filter box. Up, Down, PageUp, PageDown and Enter still work from there, and Escape empties the box before it hides the window |
| F5 (or Cmd+R on macOS) | Refresh |
| Escape | Hide the window, or cancel a form |
| Ctrl+Q | Exit |

Retry and Cancel change what runs on a CI service, so both take a modifier: a key alone, typed into the rows by someone who took the Filter box to have the keyboard, would act on the selected build.


## Command line

The `buildmonitor` command starts the tray, or shows the window of the one already running. It also takes `show`, `hide`, `quit`, `refresh`, `status` (prints the connections and builds) and `mcp` (see [MCP](mcp.md)).
