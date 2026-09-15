# Performance todo

From a review on 2026-09-15 at b97c0f2.

Timings are from a Release build on a Windows dev machine, using a stopwatch loop that warms up and then calls for 1.5 s. They are not BenchmarkDotNet results. The account is synthetic, about the size of the GitHub connection in the Debug head's log: 168 repositories, 588 workflows and 5 runs each. That is 2,940 builds, 588 selected, 271 rows and 24 visible.

Items are ordered by impact within each part. Items 1 and 2 grow with the number of pipelines. Nothing under Providers was measured: its savings are estimates from the code and the provider docs.


## App

### 1. Cache text widths in the Windows rows canvas

`RowsCanvas.OnPaint` measures about 500 strings with `TextRenderer.MeasureText` on every paint. [`ColumnWidths`](src/BuildMonitor.Windows/RowsCanvas.cs#L260) measures every distinct name, group name, detail and author across all rows, not only the visible ones. Each visible row then measures its name, each detail run twice (`DrawDetail`) and each chip twice (`DrawChips`, `Chip`).

Measured: 516 calls, 264 of them in `ColumnWidths`, and 26.3 ms per paint. The canvas repaints on every new screen, 4 times a second while visible and idle, so about 10% of a core. A hover or arrow-key repaint misses a 16 ms frame on its own.

- [x] Cache widths by string, clear the cache in `OnFontChanged`, and cap its size, since the detail runs are concatenations that vary by row. The same widths from a dictionary took 0.011 ms.

### 2. Rebuild the screen less, and sort the builds once per rebuild

[`MonitorProgram.Loop`](src/BuildMonitor.Core/Session/MonitorProgram.cs#L157) asks `ScreenCache` for a screen every frame, and [`ScreenCache.Get`](src/BuildMonitor.Core/Session/ScreenCache.cs#L22) rebuilds on every 250 ms tick, hidden or not. While hidden no one sees the screen, and the tray model and the notification take no clock.

Each rebuild sorts every build three times. [`ScreenBuilder.Build`](src/BuildMonitor.Core/Session/ScreenBuilder.cs#L15) calls `RowProjection.Builds` through `Tray`, through `RowProjection.Rows`, and directly. With a search typed, `Sized` calls `RowProjection.Rows(state with { Search = "" })`, which is a fourth.

| Call | Time | Allocated |
|---|---|---|
| `RowProjection.Builds` | 1.7 ms | 1.1 MB |
| `RowProjection.Rows` | 2.0 ms | 1.4 MB |
| `ScreenBuilder.Build` | 5.5 ms | 3.7 MB |
| `ScreenBuilder.Build`, search typed | 7.3 ms | 5.0 MB |

Hidden in the tray, that is about 22 ms of CPU and 15 MB of garbage every second.

- [x] Ignore the clock tick in `ScreenCache.Get` while `state.Hidden`. Showing the window changes the state, which rebuilds.
- [x] Compute `RowProjection.Builds` once in `Build` and hand it to `Tray`, `Rows` and `Sized`. That saves about half of each rebuild.
- [x] Tick on whole seconds rather than every 250 ms. Every time on screen is in whole seconds (`Progress.Age`, `Progress.Format`), so nothing visible changes, and a visible window rebuilds, and on Windows paints, a quarter as often. Keep the faster tick while `BuildsPage.Loading`, since the Windows spinner only turns when the canvas repaints. `ScreenCacheTests` covers the tick.

### 3. Stop the schedule table filling the log

[`Logging.Init`](src/BuildMonitor.Core/App/Logging.cs#L13) sets the minimum level to Debug in every build. Every cycle that fetches anything logs [`PollSchedule.Describe`](src/BuildMonitor.Core/Polling/ConnectionPoller.cs#L429), a line per group, and `Describe` runs before `Log.Debug` checks the level.

Measured in `src/BuildMonitor.Windows/bin/Debug/net10.0/logs/log_005.txt` from 14 Sep, with two connections of 167 and 100 groups: 953 KB of the 1,005 KB file was table rows, 97 tables in 6 min 38 s. With 1 MB files and 10 kept, the logs hold about an hour at that rate, so Open logs and Raise issue can miss anything older.

- [x] Information as the minimum in Release, with Debug still reachable through a Debug build or an environment variable.
- [x] Build the table only when `Log.IsEnabled(LogEventLevel.Debug)`.

### 4. Cache secrets in memory

[`ConnectionPoller.Cycle`](src/BuildMonitor.Core/Polling/ConnectionPoller.cs#L224) reads the token from the secret store at the start of every cycle, before it knows whether anything is due. On macOS each read runs `/usr/bin/security` ([`KeychainSecretStore`](src/BuildMonitor.Core/Auth/KeychainSecretStore.cs#L13)). On Linux with the Secret Service it runs `secret-tool` ([`SecretToolSecretStore`](src/BuildMonitor.Core/Auth/SecretToolSecretStore.cs#L14)). The log in item 3 has a fetching cycle about every 4 s across its two connections, and cycles that fetch nothing are not logged. On Windows a read is a file and DPAPI, which is cheap.

Not measured: it needs a Mac or a Linux desktop.

- [x] A caching `ISecretStore` around the store [`SecretStores.ForPlatform`](src/BuildMonitor.Core/Auth/SecretStores.cs#L8) returns. `Read` serves a value from memory after the first read. `Write` and `Delete` go to the inner store first, then update memory. `SignInCoordinator`, `TokenRefresher` and `RealActions` all write through the one instance `MonitorProgram.RunOwned` builds, so the cache cannot go stale in process. A miss need not be cached: a connection with no token waits for a wake rather than cycling.

### 5. Hold less in the ETag cache

[`HttpJson.GetBytes`](src/BuildMonitor.Core/Providers/HttpJson.cs#L90) keeps the whole body of every GET that carries an ETag in [`ETagCache`](src/BuildMonitor.Core/Providers/ETagCache.cs#L15), and a 304 hands the bytes back to be parsed again. GitHub asks for a `per_page` of 5 times the repository's workflows ([`GitHubProvider.FetchBuilds`](src/BuildMonitor.Core/Providers/GitHub/GitHubProvider.cs#L162)), and a live public runs response measured 12.2 KB per run. About 570 workflows is up to about 35 MB of byte arrays, held for as long as the connection polls. Any body over 85 KB, which is a repository with two or more workflows, is on the large object heap.

Measured on a 243.9 KB response of 20 runs: parsing takes 0.12 ms and allocates 26 KB, so a 304 is cheap on CPU. The parsed `GitHubRuns` retains 24.7 KB, a tenth of its body.

- [x] Cache the parsed value per URL and type, and return it on a 304 without parsing. The DTOs have setters, and GitLab's REST fallback writes a pipeline's timings into the parsed list ([`GitLabProvider.Rest`](src/BuildMonitor.Core/Providers/GitLab/GitLabProvider.cs#L128)), so copy the run there, and check the other providers, before sharing parsed values across cycles. `GetText` is only called from `HttpJsonTests`, some of which exercise the cache through it.
- Or keep the bytes but compress them (Brotli at its fastest level). That avoids the sharing question and keeps the parse on every 304.

### 6. Do not redraw unchanged frames in the native heads

[`NativeMonitorWindow.Present`](src/BuildMonitor.Native/Native/NativeMonitorWindow.cs#L82) flattens the screen every frame, even when `ScreenCache` handed back the same instance. [`ScreenPayload.WithPinned`](src/BuildMonitor.Native/Native/ScreenPayload.cs#L209) then copies each of its twelve lists into a new array.

Measured: `ScreenPayload.Build` takes 0.036 ms and allocates 23.5 KB, about 1.4 MB of garbage a second at 60 fps, before the array copies.

The larger cost is native and was not measured. On macOS, [`Runtime.present`](native/swift/Sources/Bm/Runtime.swift#L102) decodes every string into a `Frame`, then sets `needsDisplay` and calls `displayIfNeeded`, a full redraw every frame. On Linux, [`bm_present`](native/src/bm.cpp#L1080) runs a whole ImGui frame and draw at `SetTargetFPS(60)`.

- [x] Managed: rebuild the payload only when the screen instance changes, and pin the lists through `CollectionsMarshal.AsSpan` rather than copying them.
- [x] Native: a changed flag or generation in `BmScreen`, so that with nothing changed and no input the native side only pumps events. ImGui still wants frames while an item is active or the pointer moves, and while the spinner shows. Needs a `BM_VERSION` bump and a run of the build-native workflow. Done in source; the committed binaries still need that run.

### Smaller

- [x] [`RowsCanvas.OnPaint`](src/BuildMonitor.Windows/RowsCanvas.cs#L241) creates the selected-row and hover-row `SolidBrush` on every paint and never disposes them.
- [x] On Linux, [`SniTray.Apply`](src/BuildMonitor.Native/Linux/SniTray.cs#L163) builds a new `DbusMenuLayout` on every screen rebuild. Each [`DbusMenuNode`](src/BuildMonitor.Native/Linux/DbusMenuLayout.cs#L54) reads its PNG from the embedded resources in its initializer, before the signature comparison throws the layout away. Compare the items first, or cache the icon bytes.
- [x] [`MonitorSession.Follow`](src/BuildMonitor.Core/Session/MonitorSession.cs#L97) projects the rows three times: before, after, and again in `Clamp`, whose rows are the same as after's. `ApplyPoll` measured 7.1 ms and 4.6 MB, all inside the `SessionHost` lock the frame loop takes every frame. Hand `Clamp` the row count to drop one.


## Providers

### 7. Let a probe stretch the idle cap

AppVeyor, Azure DevOps, GoCD and Jenkins all implement `RecentActivity`, and a group whose token moves is fetched at once. But only Bitbucket sets `IdleCap` ([`ProviderDescriptors`](src/BuildMonitor.Core/Providers/ProviderDescriptors.cs#L195)), so [`PollSchedule.PipelineInterval`](src/BuildMonitor.Core/Polling/PollSchedule.cs#L234) still fetches every quiet group on the others every 5 minutes, whatever the probe says. None of them send ETags, so each of those is a full response. For example, 300 quiet Jenkins jobs cost 60 requests a minute, and at a 30 minute cap they would cost 10.

- [x] `IdleCap` of 30 minutes for AppVeyor, Azure DevOps and GoCD, as Bitbucket has.
  - Trade-off: what a probe cannot see waits up to 30 minutes rather than 5. On AppVeyor that is a build starting on another branch while a newer build exists. On Azure DevOps it is a retry from the web, which reuses its build and so probably its queue time.
- [x] Jenkins too, once its probe reaches as deep as discovery does (item 10). Jobs deeper than three folders have no token today.
- [x] Travis has no probe. [`travis.md`](docs/providers/travis.md) records a listing that works live, `repos?repository.active=true&sort_by=current_build:desc&include=repository.last_started_build`, which could be one, then the same cap.
- [x] Update `docs/mdsource/options.source.md`, `docs/mdsource/troubleshooting.source.md` and the provider pages, which say five minutes, or thirty on Bitbucket.

### 8. Fetch due groups concurrently

[`FetchConcurrency`](src/BuildMonitor.Core/Providers/ProviderDescriptor.cs#L36) defaults to 1, and only GitHub sets more. [`ConnectionPoller.Fetch`](src/BuildMonitor.Core/Polling/ConnectionPoller.cs#L305) applies the rows only once every due group has finished, so a first poll of 300 Jenkins jobs, one at a time, leaves the page empty for an estimated 45 s, and a Refresh leaves it stale for as long. Concurrency does not change the number of requests, only how soon they finish.

- [x] 4 for Jenkins and GoCD, which are self hosted and may be small, and 8 for AppVeyor, Azure DevOps, Bitbucket and Travis.

### 9. Run Azure DevOps' per-project loops concurrently

[`AzureDevOpsProvider.RecentActivity`](src/BuildMonitor.Core/Providers/AzureDevOps/AzureDevOpsProvider.cs#L90) awaits one request per project in turn, every poll interval, and the cycle waits for the probe before it plans ([`ConnectionPoller.Fetch`](src/BuildMonitor.Core/Polling/ConnectionPoller.cs#L301)). With 50 projects that is an estimated 15 s before anything is fetched. Discovery ([`DiscoverPipelines`](src/BuildMonitor.Core/Providers/AzureDevOps/AzureDevOpsProvider.cs#L21)) has the same loop.

- [x] `Concurrently.Map` in both. The throughput units spent do not change.

### 10. Jenkins: ask for less, and reach deeper in one request

- [x] [`buildFields`](src/BuildMonitor.Core/Providers/Jenkins/JenkinsProvider.cs#L14) asks for `estimatedDuration` on all five builds of a job. [`jenkins.md`](docs/providers/jenkins.md) already notes that each walks up to six earlier builds, and only a running build uses an estimate. Ask once per job through `lastBuild[number,estimatedDuration]` and apply it to that build.
- [x] [`jobFields`](src/BuildMonitor.Core/Providers/Jenkins/JenkinsProvider.cs#L13) asks for `color`, which nothing reads: `JenkinsNode.Color` is declared and never used. Computing it probably loads each job's last build (not checked).
- [x] `actions[lastBuiltRevision[branch[name]]]` is unused for a multibranch job, whose branch comes from the pipeline ([`Branch`](src/BuildMonitor.Core/Providers/Jenkins/JenkinsProvider.cs#L185)).
- [x] Discovery ([`Walk`](src/BuildMonitor.Core/Providers/Jenkins/JenkinsProvider.cs#L35)) asks each folder deeper than three levels for its jobs, one request after another, and the probe ([`RecentActivity`](src/BuildMonitor.Core/Providers/Jenkins/JenkinsProvider.cs#L73)) never sees below three levels. A deeper `tree` in the discovery, folder and probe requests covers them in one.

### 11. TeamCity: fetch a project rather than the whole server

TeamCity is `FetchUnit.Connection`, so the whole server is one group, and [`FetchBuilds`](src/BuildMonitor.Core/Providers/TeamCity/TeamCityProvider.cs#L47) lists every configuration's builds in one request. While anything on the server is queued or running, that is every 10 to 30 s. [`teamcity.md`](docs/providers/teamcity.md) puts a build at about 0.38 KB, so 500 configurations is about a megabyte a poll, and TeamCity sends no ETags.

The reason [`FetchUnit`](src/BuildMonitor.Core/Providers/FetchUnit.cs#L2) gives for one group, a listing sized by the number of pipelines asked for, no longer holds: the nested `count:5` locator is per configuration.

- [x] Group by project id (`Pipeline.Group`, since the project name is not unique) and fetch `buildTypes?locator=project:(id:X)` with the same nested locator. That needs a fetch unit keyed on the group rather than the repository name.
- [x] A `sinceBuild:(id:N)` probe, which `teamcity.md` records as checked live, shaped like Azure DevOps' `minTime` one, so a quiet project waits for news. The same page notes that it does not see a state change of an older build.

Estimated saving: with 50 projects and 3 busy, about 1 MB a poll becomes about 60 KB. Medium confidence, since the probe is new code.

### 12. GitHub: list workflows only for pushed repositories

[`DiscoverPipelines`](src/BuildMonitor.Core/Providers/GitHub/GitHubProvider.cs#L50) lists the workflows of every active repository every 10 minutes. They answer 304, but the secondary limit counts a 304, and [`Spend`](src/BuildMonitor.Core/Polling/ConnectionPoller.cs#L657) charges discovery to the request bucket, which defers due groups. For 168 repositories that is 168 requests in one cycle, 37% of the 450 a minute quota.

- [x] Keep each repository's `pushed_at` from the last discovery, list workflows only for repositories pushed since, and do a full pass every hour.
  - Not checked: whether `pushed_at` moves for a push to a branch other than the default. It probably does not move when a workflow is enabled or disabled, which the hourly pass would pick up.

### 13. Octopus: remember that the dashboard is too small

When a server's `ProjectLimit` is below the project count, [`FetchBuilds`](src/BuildMonitor.Core/Providers/Octopus/OctopusProvider.cs#L52) downloads the dashboard every cycle and throws it away before using the separate listings. Those re-read `environments/all` every cycle ([`Separately`](src/BuildMonitor.Core/Providers/Octopus/OctopusProvider.cs#L116)) and fetch each task missing from the tasks page one at a time ([`Separately`](src/BuildMonitor.Core/Providers/Octopus/OctopusProvider.cs#L140)).

- [x] Remember the limit until the next discovery, keep the environments until then, and fetch missing tasks together with `{space}/tasks?ids=a,b,c`, which [`octopus.md`](docs/providers/octopus.md) records as checked live. That saves two requests a cycle, one of them the largest, and all but one of the task requests. How often a server hits the limit is unknown.

### 14. GitLab: remember that GraphQL is unavailable

When GraphQL fails, [`FetchBuilds`](src/BuildMonitor.Core/Providers/GitLab/GitLabProvider.cs#L41) sends it again every cycle, then fetches each project over REST one at a time, plus a request per running or pending pipeline. The whole connection is one group, so while anything runs that is about a request per project every 10 to 30 s.

- [x] Remember for an hour that GraphQL failed, and send the REST requests through `Concurrently.Map`. How often a server lacks GraphQL is unknown.
- [x] Minor: the GraphQL URL lists project ids in discovery order, most recently active first, so a rediscovery that reorders them misses the cached ETags. Sorting the ids keeps the URLs stable. That saves bytes only, since a 304 counts against GitLab's limit.

### 15. Bitbucket: ask for the fields that are read

Discovery ([`DiscoverPipelines`](src/BuildMonitor.Core/Providers/Bitbucket/BitbucketProvider.cs#L15)) and the pipelines request ([`FetchBuilds`](src/BuildMonitor.Core/Providers/Bitbucket/BitbucketProvider.cs#L38)) return whole objects, where the code reads four repository fields and about ten pipeline fields. The probe already uses `fields=` ([`RecentActivity`](src/BuildMonitor.Core/Providers/Bitbucket/BitbucketProvider.cs#L56)).

- [x] `fields=next,values.slug,values.full_name,values.links.html.href` on discovery, and `fields=values.uuid,values.build_number,values.state,values.target.ref_type,values.target.ref_name,values.target.commit.hash,values.target.pullrequest.id,values.creator.display_name,values.created_on,values.completed_on` on pipelines. The same requests, with smaller 200s. Not checked: `fields` on the pipelines endpoint, or how much smaller.

### 16. GoCD: let the dashboard decide when a running pipeline is fetched

[`FetchBuilds`](src/BuildMonitor.Core/Providers/GoCd/GoCdProvider.cs#L84) fetches a running pipeline's history, a full response, every 10 to 30 s. Its row only changes when a stage does, and the dashboard token already reports stage statuses, often as a 304. The elapsed time on a running row comes from the clock, so it keeps moving without a fetch.

- [ ] A descriptor flag so that, for GoCD, a probe at the running interval decides when a running pipeline is fetched, rather than the schedule. Low confidence, as it changes the schedule. Skipped.


## Found in passing

- [x] Octopus: [`Separately`](src/BuildMonitor.Core/Providers/Octopus/OctopusProvider.cs#L140) fetches a missing task as `tasks/{id}`, without the space. `FetchLog`'s comment says a route without the space reads the default space only, so in a named space this probably fails the fetch with a 404. Only on the fallback path of item 13, for a task outside the tasks page. Not reproduced.


## Benchmarks project

- [x] An on-demand BenchmarkDotNet project with a large-account fixture of the shape above, covering `ScreenBuilder.Build` with and without a search, `MonitorSession.ApplyPoll` and `NextRow`, and the Windows canvas text measuring, which needs `net10.0-windows`. Keep it out of `dotnet test` and CI, whose runners are too noisy to gate on. Core, the Windows head and the native head grant internals by assembly name, so each needs an `InternalsVisibleTo` for it.
