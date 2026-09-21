/// <summary>
/// A tray started so that something can be asked of it, rather than because the user asked for
/// BuildMonitor, opens no window. The MCP server starts one on demand, and taking the screen to
/// answer a question is not what was asked for.
/// </summary>
public class HiddenStartTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task HiddenBeatsShowWindowAtStart(bool showWindowAtStart)
    {
        var settings = new Settings
        {
            ShowWindowAtStart = showWindowAtStart
        };

        await Assert.That(MonitorProgram.StartState(settings, hidden: true).Hidden).IsTrue();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task WithoutItTheSettingDecides(bool showWindowAtStart, bool expected)
    {
        var settings = new Settings
        {
            ShowWindowAtStart = showWindowAtStart
        };

        await Assert.That(MonitorProgram.StartState(settings, hidden: false).Hidden).IsEqualTo(expected);
    }

    /// <summary>
    /// The run's own visibility is not the user's preference. Written to the settings instead, a
    /// hidden start would be saved for good the next time the options page was saved.
    /// </summary>
    [Test]
    public async Task AHiddenStartDoesNotChangeTheSavedSetting()
    {
        var settings = new Settings
        {
            ShowWindowAtStart = true
        };

        var state = MonitorProgram.StartState(settings, hidden: true);

        await Assert.That(state.Hidden).IsTrue();
        await Assert.That(state.Settings.ShowWindowAtStart).IsTrue();
        await Assert.That(OptionsDraft.TryBuild(Fixtures.OptionsForm(MonitorSession.OpenOptions(state)), state.Settings, out var saved, out _)).IsTrue();
        await Assert.That(saved!.ShowWindowAtStart).IsTrue();
    }
}
