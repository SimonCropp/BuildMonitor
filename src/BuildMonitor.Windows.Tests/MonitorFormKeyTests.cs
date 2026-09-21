/// <summary>
/// The Windows head's keymap. The keys that change what runs on a CI service take Control: R
/// alone, typed into the rows by someone who took the filter box to have the keyboard, reran the
/// selected build.
/// </summary>
public class MonitorFormKeyTests
{
    [Test]
    [Arguments(Keys.R, true, false, CommandKind.Retry)]
    [Arguments(Keys.R, false, false, CommandKind.None)]
    [Arguments(Keys.OemPeriod, true, false, CommandKind.Cancel)]
    [Arguments(Keys.OemPeriod, false, false, CommandKind.None)]
    [Arguments(Keys.L, true, false, CommandKind.CopyLog)]
    [Arguments(Keys.T, true, false, CommandKind.Triage)]
    [Arguments(Keys.F10, false, true, CommandKind.OpenMenu)]
    [Arguments(Keys.F10, false, false, CommandKind.None)]
    [Arguments(Keys.Apps, false, false, CommandKind.OpenMenu)]
    [Arguments(Keys.C, true, false, CommandKind.CopyBuildUrl)]
    [Arguments(Keys.Enter, false, false, CommandKind.OpenBuild)]
    [Arguments(Keys.F5, false, false, CommandKind.Refresh)]
    [Arguments(Keys.Escape, false, false, CommandKind.Hide)]
    public async Task OnTheBuildsPage(Keys key, bool control, bool shift, CommandKind command) =>
        await Assert.That(MonitorForm.Command(key, control, shift, form: false)).IsEqualTo(command);

    /// <summary>
    /// A form's page has no selected build, so the keys that act on one do nothing there.
    /// </summary>
    [Test]
    [Arguments(Keys.R, true, false, CommandKind.None)]
    [Arguments(Keys.OemPeriod, true, false, CommandKind.None)]
    [Arguments(Keys.Apps, false, false, CommandKind.None)]
    [Arguments(Keys.Escape, false, false, CommandKind.CancelForm)]
    public async Task OnAForm(Keys key, bool control, bool shift, CommandKind command) =>
        await Assert.That(MonitorForm.Command(key, control, shift, form: true)).IsEqualTo(command);
}
