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
    /// Counts alone put "0 failing" beside the Attention icon, which reads as all clear.
    /// </summary>
    [Test]
    public async Task ASignInLeads()
    {
        var tray = ScreenBuilder.Tray(Fixtures.NeedsAuth());
        await Assert.That(tray.Icon).IsEqualTo(TrayIconKind.Attention);
        await Assert.That(tray.Tooltip).IsEqualTo("BuildMonitor: sign in required for GitHub. Verify failing, 4 running");
    }

    /// <summary>
    /// The poller waits a rate limit out by itself, so it raises no Attention, but a hover asking
    /// why the rows stopped changing still finds it.
    /// </summary>
    [Test]
    public async Task ARateLimitIsSaidButRaisesNoAttention()
    {
        var tray = ScreenBuilder.Tray(Fixtures.RateLimited());
        await Assert.That(tray.Icon).IsEqualTo(TrayIconKind.Failed);
        await Assert.That(tray.Tooltip).IsEqualTo("BuildMonitor: GitHub rate limited. Verify failing, 4 running");
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
