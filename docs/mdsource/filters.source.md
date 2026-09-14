# Filters

Windows:

<img src="../src/BuildMonitor.Windows.Tests/MonitorFormTests.Filters.verified.png">

macOS:

<img src="../src/BuildMonitor.Tests/Native/PixelTests.Filters.OSX.verified.png">

Linux:

<img src="../src/BuildMonitor.Tests/Native/PixelTests.Filters.Linux.verified.png">

A filter excludes what it matches. Each one has:

 * a target: the pipeline name, the repository name, or the branch name
 * a match: exact, prefix, suffix or contains
 * the text, compared without regard to case

Pipeline and repository filters are applied before a pipeline is fetched, so an excluded pipeline costs no API calls. Branch filters apply to the builds that come back.

The context menu's "Exclude" on a row adds an exact pipeline filter for it at once.

Filters are saved in settings.json and apply to every connection.
