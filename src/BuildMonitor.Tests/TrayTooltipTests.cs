/// <summary>
/// What a hover on the tray icon says, which while the window is hidden is the whole of the app.
/// </summary>
public class TrayTooltipTests
{
    [Test]
    public async Task NoConnections() =>
        await Assert.That(ScreenBuilder.Tray(Fixtures.Empty()).Tooltip).IsEqualTo("BuildMonitor: no connections");

    [Test]
    public async Task NothingFailing() =>
        await Assert.That(ScreenBuilder.Tray(Fixtures.Connected()).Tooltip).IsEqualTo("BuildMonitor: 0 failing, 0 running");

    /// <summary>
    /// By the name its row leads with, so the hover and the window agree on what is broken.
    /// </summary>
    [Test]
    public async Task AFailureIsNamed() =>
        await Assert.That(ScreenBuilder.Tray(Fixtures.WithBuilds()).Tooltip).IsEqualTo("BuildMonitor: Verify failing, 4 running");

    /// <summary>
    /// Two of its pipelines failing is still one project to go and look at.
    /// </summary>
    [Test]
    public async Task AProjectIsNamedOnce() =>
        await Assert.That(ScreenBuilder.Tray(Fixtures.WithTwoFailures()).Tooltip).IsEqualTo("BuildMonitor: Verify failing, 4 running");

    [Test]
    public async Task TwoProjectsAreJoined() =>
        await Assert.That(ScreenBuilder.Tray(Fixtures.WithDependabotFailure()).Tooltip).IsEqualTo("BuildMonitor: Reports and Verify failing, 4 running");

    /// <summary>
    /// Every health one connection can be in, on its own: what the icon does and what a hover
    /// says. A connection that needs something done leads the tooltip and raises Attention, since
    /// counts alone put "0 failing" beside that icon, which reads as all clear. The poller waits a
    /// rate limit out by itself, so that raises no Attention, but a hover asking why the rows have
    /// stopped changing still finds it. A poll in flight is the footer's to say, not the tray's.
    /// </summary>
    [Test]
    [Arguments(ConnectionHealth.Unpolled, TrayIconKind.Failed, "BuildMonitor: Verify failing, 4 running")]
    [Arguments(ConnectionHealth.Ok, TrayIconKind.Failed, "BuildMonitor: Verify failing, 4 running")]
    [Arguments(ConnectionHealth.Polling, TrayIconKind.Failed, "BuildMonitor: Verify failing, 4 running")]
    [Arguments(ConnectionHealth.Error, TrayIconKind.Attention, "BuildMonitor: error polling GitHub. Verify failing, 4 running")]
    [Arguments(ConnectionHealth.NeedsAuth, TrayIconKind.Attention, "BuildMonitor: sign in required for GitHub. Verify failing, 4 running")]
    [Arguments(ConnectionHealth.RateLimited, TrayIconKind.Failed, "BuildMonitor: GitHub rate limited. Verify failing, 4 running")]
    public async Task EachHealthOnItsOwn(ConnectionHealth health, TrayIconKind icon, string tooltip)
    {
        var tray = ScreenBuilder.Tray(MonitorSession.SetHealth(Fixtures.WithBuilds(), Fixtures.GitHub.Id, health));
        await Assert.That(tray.Icon).IsEqualTo(icon);
        await Assert.That(tray.Tooltip).IsEqualTo(tooltip);
    }

    /// <summary>
    /// GitHub sorts ahead of Jenkins, but only Jenkins raised the icon, so it is the one named.
    /// </summary>
    [Test]
    public async Task WhatRaisedTheIconLeads()
    {
        var tray = ScreenBuilder.Tray(Fixtures.ConnectionErrors());
        await Assert.That(tray.Icon).IsEqualTo(TrayIconKind.Attention);
        await Assert.That(tray.Tooltip).IsEqualTo("BuildMonitor: error polling Jenkins (+1 more). Verify failing, 4 running");
    }

    /// <summary>
    /// Newest first, as the rows are, and whole names only: the Windows head cuts at the limit,
    /// which would halve one.
    /// </summary>
    [Test]
    public async Task NamesComeOffTheEndToFit()
    {
        var failures = Enumerable.Range(0, 6)
            .Select(_ => Fixtures.Build(
                Fixtures.GitHub.Id,
                $"Long{_}/build.yml",
                "build.yml",
                $"VerifyTests/RepositoryWithAFairlyLongName0{_}",
                "main",
                "1",
                BuildStatus.Failed,
                started: Fixtures.Now - TimeSpan.FromMinutes(_ + 1),
                finished: Fixtures.Now - TimeSpan.FromMinutes(_)));
        var state = MonitorSession.ApplyPoll(Fixtures.WithBuilds(), Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), ..failures], Fixtures.Now);
        var tooltip = ScreenBuilder.Tray(state).Tooltip;
        await Assert.That(tooltip).IsEqualTo("BuildMonitor: RepositoryWithAFairlyLongName00, RepositoryWithAFairlyLongName01 and 5 more failing, 4 running");
        await Assert.That(tooltip.Length).IsLessThanOrEqualTo(ScreenBuilder.TrayTooltipLimit);
    }

    /// <summary>
    /// A first name too long for the line leaves nothing to list, so the failures are counted as
    /// the header counts them.
    /// </summary>
    [Test]
    public async Task ANameTooLongToFitIsCounted()
    {
        var failure = Fixtures.Build(
            Fixtures.GitHub.Id,
            "Long/build.yml",
            "build.yml",
            $"VerifyTests/{new string('a', 120)}",
            "main",
            "1",
            BuildStatus.Failed,
            started: Fixtures.Now - TimeSpan.FromMinutes(1),
            finished: Fixtures.Now);
        var state = MonitorSession.ApplyPoll(Fixtures.WithBuilds(), Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), failure], Fixtures.Now);
        await Assert.That(ScreenBuilder.Tray(state).Tooltip).IsEqualTo("BuildMonitor: 2 failing, 4 running");
    }
}
