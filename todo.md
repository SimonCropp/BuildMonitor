# UX todo

What is left from the UX review of 2026-09-21. The fixes it led to landed in 3db6da9: field notes,
poll progress in the footer, problems no longer hidden behind a status message, and a failure
notification that opens its build. Worst first within each section.


## Follow-ups from 3db6da9

- [x] **Draw field notes on macOS and Linux.** Done in 33d77af, with BM_VERSION 14 and binaries
  from #24. The same rebuild carried 58e6ce7, which fixes the macOS window never opening on a
  fresh start, and 3286e63, which rebaselines the native Options captures that had been failing
  on main since 0f2420a.
- [x] **Say that the pixel baselines never run in CI.** [claude.md](claude.md) now has the Debug
  command beside the other test commands, and a Pixel snapshots note on why the WinForms baselines
  need it while the native ones come from CI.


## Broken

- [x] **The tray tooltip ignores connection health.** The tooltip now leads with what raised the
  icon ("sign in required for GitHub"), names failing projects as their rows do, and fits in 127
  characters by dropping names as "and N more". A rate limit is said but still raises no Attention:
  the poller waits it out by itself.


## Destructive actions with no guard

- [x] **Remove connection is one click, next to Cancel.** Remove now opens a page naming the
  connection and what removing it costs; only its own Remove deletes anything, and Cancel or Escape
  go back to the editor with its edits.
- [x] **The row menu has no separators, and excludes cannot be undone.** Lines now part what to
  look at, what changes a service's builds, what changes this window, and the excludes (BM_VERSION
  15). An exclude leaves an Undo in the footer for as long as its message stands.
- [x] **Cancel, Retry and Run next are one click on rows that move.** A poll records the visible
  positions it changed; for 1.5s a Retry, Cancel or Run next click there is held with a status
  naming what a second click does. Holding the order under the pointer would have needed an ABI
  change and frozen the sort under a parked mouse.


## Navigation

- [x] **The connection editor always exits to Builds.** The editor holds the options page it was
  opened from, and Save, Cancel and Remove go back to it.
- [x] **Opening a connection editor throws away unsaved options.** Fixed by the same change: the
  options go back as they were left, edits and all.
- [x] **"Sign in required" in the footer cannot be clicked.** The footer carries Sign in, or Check
  connection for an error, opening that connection's editor. The footer, its tooltip and the tray
  now name connections that need the user before rate limits.


## Discoverability and polish

- [x] **Keyboard coverage is thin and undocumented.** Retry is Ctrl+R (Shift+Cmd+R on macOS);
  Cancel, Copy log and Triage are Ctrl+., Ctrl+L and Ctrl+T; Shift+F10 or the Menu key opens the
  row's menu; the tray docs list every key. Enter already toggled groups.
- [x] **Options is one flat column, and Add connection is buried.** Connections is a page of its
  own, reached from the footer and the tray, and with none the builds footer leads with Add
  connection. The settings left on Options sit under General, Builds, Polling and Local headings.
- [x] **Options mixes saved settings with immediate actions.** Version, Documentation, Open logs,
  Raise issue and Update are an About page, opened from the options' footer. Its Back, and an
  update called off from it, return to the options with their edits.
- [x] **Validation errors show at the bottom of the form.** A form's error now carries the field it
  is about and is drawn under it: a missing token under the token box, a bad poll interval under
  Polling, a failed sign in under Sign in with. Only an error about the whole form, such as a test
  the service refused, stays at the foot. A missing value is named by its label at the start of the
  sentence, as "API token is required.", since lowering only a first letter would still turn
  "Atlassian account email" into "atlassian".
- [ ] **Window size, position and open groups reset on every start.** The window opens at
  `CenterScreen` at a fixed size ([MonitorForm.cs](src/BuildMonitor.Windows/MonitorForm.cs)), and
  `OpenGroups` lives in `SessionState` rather than `Settings`. For an app that runs at login and is
  shown and hidden all day, both reset every morning.
