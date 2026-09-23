#if DEBUG
/// <summary>
/// The WinForms head drawing each canonical state. These are the pictures the docs show.
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
[NotInParallel(Painting.Key)]
public class MonitorFormTests
{
    [Test]
    public Task Builds() =>
        Capture(Fixtures.WithBuilds());

    // What the readme shows: running builds, a failure, a closed group and the filter box in use.
    [Test]
    public Task Searched() =>
        Capture(MonitorSession.Search(Fixtures.WithTwoFailures(), "main"));

    [Test]
    public Task Empty() =>
        Capture(Fixtures.Empty());

    // A pipeline's own row on its main, and its pull requests that are running, queued or failed on
    // the rows beneath it, their first cell and marks left to the row above. What the docs show for
    // other branches.
    [Test]
    public Task Lanes() =>
        Capture(Fixtures.WithLanes());

    [Test]
    public Task Groups() =>
        Capture(Fixtures.WithTwoFailures());

    // A prefix group, open: it names no one repository, so its row has no mark to lead with and
    // its members name the repositories they came from.
    [Test]
    public Task PrefixGroup() =>
        Capture(MonitorSession.ToggleGroup(Fixtures.WithPrefixGroup(), Fixtures.VerifyPassing));

    // An Octopus project group, open. Octopus reports no repository, so neither the group's row nor
    // its members carry a mark before the name; the project each deployed is the name itself.
    [Test]
    public Task ProjectGroup() =>
        Capture(MonitorSession.ToggleGroup(Fixtures.WithProjectGroup(), Fixtures.StorefrontPassing));

    // The rows of the repositories found under the code directory, whose chip is a folder rather
    // than a word. The only baseline that shows it drawn.
    [Test]
    public Task LocalRepos() =>
        Capture(Fixtures.WithLocalRepos());

    [Test]
    public Task NeedsAuth() =>
        Capture(Fixtures.NeedsAuth());

    [Test]
    public Task Connections() =>
        Capture(Fixtures.Connections());

    [Test]
    public Task Options() =>
        Capture(Fixtures.Options());

    [Test]
    public Task Filters() =>
        Capture(Fixtures.Filters());

    [Test]
    public Task ConnectionNew() =>
        Capture(Fixtures.ConnectionNew());

    [Test]
    public Task ConnectionEdit() =>
        Capture(Fixtures.ConnectionEdit());

    [Test]
    public Task SignInDevice() =>
        Capture(Fixtures.SignInDevice());

    // Every line about the MCP servers shares an id, which left all but the last of them blank.
    [Test]
    public Task Update() =>
        Capture(Fixtures.Update());

    [Test]
    public Task BuildsLight() =>
        Capture(Fixtures.WithBuilds(), Theme.Light);

    // Too narrow for the bar, which gives way before the chips or the names.
    [Test]
    public Task Narrow() =>
        Capture(Fixtures.WithBuilds(), width: 760);

    // Pinned rather than System, so a capture does not depend on the theme of whoever ran it.
    static async Task Capture(SessionState state, Theme theme = Theme.Dark, int width = 1000)
    {
        state = state
            with
            {
                Settings = state.Settings
                    with
                    {
                        Theme = theme
                    }
            };
        using var form = new MonitorForm(ScreenBuilder.Title, width, 640);
        // Shown, off screen: a drop down list only paints its text once it has a handle and has
        // been laid out, and DrawToBitmap of a form that was never shown leaves it blank.
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new(-20000, -20000);
        form.Show();
        form.Apply(ScreenBuilder.Build(state, Fixtures.Now));
        Application.DoEvents();
        await Verify(form);
        form.AllowClose = true;
    }
}
#endif
