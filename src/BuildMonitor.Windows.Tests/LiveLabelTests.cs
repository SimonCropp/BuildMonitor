[TUnit.Core.Executors.STAThreadExecutor]
[NotInParallel(nameof(LiveLabelTests))]
public class LiveLabelTests
{
    const int styleIndex = -16;
    const int visibleStyle = 0x10000000;

    /// <summary>
    /// Turning redraw back on shows the window it is sent to, so a hidden label that took the
    /// quiet path to a new text would come back on screen with it.
    /// </summary>
    [Test]
    public async Task SettingTheTextLeavesAHiddenLabelHidden()
    {
        using var label = new LiveLabel
        {
            Visible = false
        };
        _ = label.Handle;
        label.Text = "GitHub Actions: polling 66/152";
        await Assert.That(label.Text).IsEqualTo("GitHub Actions: polling 66/152");
        await Assert.That(GetWindowLong(label.Handle, styleIndex) & visibleStyle).IsEqualTo(0);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetWindowLong(IntPtr window, int index);
}
