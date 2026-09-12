/// <summary>
/// One snapshot per canonical state, rendered as text. These are the tests every head is held
/// to: a head draws exactly this structure, so a change here is a change on every platform.
/// </summary>
public class ScreenTests
{
    [Test]
    public Task Empty() =>
        Verify(Fixtures.Render(Fixtures.Empty()));

    [Test]
    public Task Connected() =>
        Verify(Fixtures.Render(Fixtures.Connected()));

    [Test]
    public Task Builds() =>
        Verify(Fixtures.Render(Fixtures.WithBuilds()));

    [Test]
    public Task Polling() =>
        Verify(Fixtures.Render(Fixtures.Polling()));

    [Test]
    public async Task ProgressIsDroppedWhenThePollEnds()
    {
        var state = Fixtures.Polling();
        await Assert.That(state.Connection(Fixtures.GitHub.Id)!.Progress).IsEqualTo(new PollProgress(15, 20));
        var done = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [], Fixtures.Now);
        await Assert.That(done.Connection(Fixtures.GitHub.Id)!.Progress).IsNull();
        // A late report after the poll finished must not resurrect it.
        var late = MonitorSession.SetProgress(done, Fixtures.GitHub.Id, new(20, 20));
        await Assert.That(late.Connection(Fixtures.GitHub.Id)!.Progress).IsNull();
    }

    [Test]
    public Task Folded() =>
        Verify(Fixtures.Render(Fixtures.Folded()));

    [Test]
    public Task Scrolled() =>
        Verify(Fixtures.Render(Fixtures.Scrolled()));

    [Test]
    public Task Narrow() =>
        Verify(Fixtures.Render(Fixtures.Narrow()));

    [Test]
    public Task MenuOpen() =>
        Verify(Fixtures.Render(Fixtures.WithMenu()));

    [Test]
    public Task HeaderMenuOpen() =>
        Verify(Fixtures.Render(MonitorSession.OpenMenu(Fixtures.WithBuilds(), 0)));

    [Test]
    public Task DefaultBranchOnly()
    {
        var state = Fixtures.WithBuilds();
        state = MonitorSession.ApplySettings(state, state.Settings with { ShowOtherBranches = false });
        return Verify(Fixtures.Render(state));
    }

    [Test]
    public Task Options() =>
        Verify(Fixtures.Render(Fixtures.Options()));

    [Test]
    public Task Filters() =>
        Verify(Fixtures.Render(Fixtures.Filters()));

    [Test]
    public Task FiltersError() =>
        Verify(Fixtures.Render(MonitorSession.AddFilter(Fixtures.Filters())));

    [Test]
    public Task ConnectionNew() =>
        Verify(Fixtures.Render(Fixtures.ConnectionNew()));

    [Test]
    public Task ConnectionNewGitHubDevice()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GitHub Actions");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Device));
        return Verify(Fixtures.Render(state));
    }

    [Test]
    public Task ConnectionEdit() =>
        Verify(Fixtures.Render(Fixtures.ConnectionEdit()));

    [Test]
    public Task ConnectionValidationError() =>
        Verify(Fixtures.Render(InputApplier.Execute(Fixtures.ConnectionNew(), CommandKind.Save, null, MonitorActions.None, null)));

    [Test]
    public Task SignInDevice() =>
        Verify(Fixtures.Render(Fixtures.SignInDevice()));

    [Test]
    public Task NeedsAuth() =>
        Verify(Fixtures.Render(Fixtures.NeedsAuth()));

    [Test]
    public Task RateLimited() =>
        Verify(Fixtures.Render(Fixtures.RateLimited()));

    [Test]
    public Task Hidden() =>
        Verify(Fixtures.Render(Fixtures.Hidden()));

    [Test]
    public Task Status() =>
        Verify(Fixtures.Render(MonitorSession.SetStatus(Fixtures.WithBuilds(), "Retrying test.yml #77")));

    [Test]
    public Task OverRun()
    {
        var state = Fixtures.WithBuilds();
        state = MonitorSession.ApplyMedians(
            state,
            ImmutableDictionary<string, TimeSpan>.Empty.Add("gh/DiffEngine/test.yml", TimeSpan.FromMinutes(2)));
        return Verify(Fixtures.Render(state));
    }
}
