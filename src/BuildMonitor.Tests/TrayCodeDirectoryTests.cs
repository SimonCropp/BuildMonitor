/// <summary>
/// The tray menu's Open code directory. Unlike the folder chip on a row, which resolves a checkout
/// from the build it sits on, this one has no build to go on and opens the option itself.
/// </summary>
public class TrayCodeDirectoryTests
{
    [Test]
    public async Task TheItemIsThereOnlyWhenTheOptionIs()
    {
        await Assert.That(Items(Fixtures.WithCodeDirectory())).Contains("Open code directory");
        await Assert.That(Items(Fixtures.WithBuilds())).DoesNotContain("Open code directory");
    }

    /// <summary>
    /// Beside Open logs, the other item that opens a folder, rather than at the end among Update
    /// and Exit.
    /// </summary>
    [Test]
    public async Task ItSitsAboveOpenLogs()
    {
        var items = Items(Fixtures.WithCodeDirectory());

        await Assert.That(items.IndexOf("Open code directory")).IsEqualTo(items.IndexOf("Open logs") - 1);
    }

    [Test]
    public async Task ClickingItOpensTheOption()
    {
        var actions = new RecordingActions();

        InputApplier.Apply(Fixtures.WithCodeDirectory(), new(TrayItem: TrayMenu.CodeDirectory), actions.Actions, new FakeWindow());

        await Assert.That(actions.Calls).IsEquivalentTo(["OpenDirectory /code"]);
    }

    /// <summary>
    /// A menu built before a save that cleared the option still carries the item, and clicking it
    /// must not ask for an empty directory.
    /// </summary>
    [Test]
    public async Task ClickingItAfterTheOptionIsClearedDoesNothing()
    {
        var actions = new RecordingActions();
        var cleared = MonitorSession.ApplySettings(Fixtures.WithCodeDirectory(), Fixtures.Settings());

        InputApplier.Apply(cleared, new(TrayItem: TrayMenu.CodeDirectory), actions.Actions, new FakeWindow());

        await Assert.That(actions.Calls).IsEmpty();
    }

    static List<string> Items(SessionState state) =>
        ScreenBuilder.Build(state, Fixtures.Now).Tray.Items.Select(_ => _.Label).ToList();
}
