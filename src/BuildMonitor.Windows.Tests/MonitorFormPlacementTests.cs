/// <summary>
/// The window opens where it was left and says where it settled, which is what keeps it from
/// opening centred at its first size every morning. Shown at no opacity where a test has to show
/// it, since a maximize or a hide only happens to a window on screen.
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
[NotInParallel(nameof(MonitorFormPlacementTests))]
public class MonitorFormPlacementTests
{
    [Test]
    public async Task OpensWhereItWasLeft()
    {
        var bounds = Inside();
        using var form = new MonitorForm(ScreenBuilder.Title, 1000, 640, PlacementOf(bounds));
        await Assert.That(form.StartPosition).IsEqualTo(FormStartPosition.Manual);
        await Assert.That(form.Bounds).IsEqualTo(bounds);
        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Normal);
    }

    [Test]
    public async Task OpensMaximizedOverWhereItWasLeft()
    {
        var bounds = Inside();
        using var form = new MonitorForm(ScreenBuilder.Title, 1000, 640, PlacementOf(bounds, maximized: true));
        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Maximized);
        await Assert.That(form.RestoreBounds).IsEqualTo(bounds);
    }

    /// <summary>
    /// A monitor unplugged since it was left there: centred at the first size, as on a first start,
    /// rather than somewhere it could not be dragged back from.
    /// </summary>
    [Test]
    public async Task OpensCentredWhenNoScreenReachesWhereItWasLeft()
    {
        using var form = new MonitorForm(ScreenBuilder.Title, 1000, 640, new(-100000, -100000, 1200, 800));
        await Assert.That(form.StartPosition).IsEqualTo(FormStartPosition.CenterScreen);
        await Assert.That(form.Location).IsNotEqualTo(new(-100000, -100000));
    }

    [Test]
    public async Task ReportsWhereItWasHidden()
    {
        using var form = Shown();
        var bounds = Inside();
        form.Bounds = bounds;
        form.Hide();
        await Assert.That(form.Drain().Placement).IsEqualTo(PlacementOf(bounds));
        // Once, as each report is a save.
        await Assert.That(form.Drain().Placement).IsNull();
        form.AllowClose = true;
    }

    /// <summary>
    /// Maximized, with the bounds a restore goes back to rather than the screen it fills, so a
    /// restore after the next start is to the size it had.
    /// </summary>
    [Test]
    public async Task ReportsAMaximizeWithTheBoundsItRestoresTo()
    {
        using var form = Shown();
        var bounds = Inside();
        form.Bounds = bounds;
        form.WindowState = FormWindowState.Maximized;
        await Assert.That(form.Drain().Placement).IsEqualTo(PlacementOf(bounds, maximized: true));
        form.AllowClose = true;
    }

    /// <summary>
    /// Minimized is no place to open, so it reports nothing, and what was last reported stands.
    /// </summary>
    [Test]
    public async Task ReportsNothingForAMinimize()
    {
        using var form = Shown();
        form.Drain();
        form.WindowState = FormWindowState.Minimized;
        await Assert.That(form.Drain().Placement).IsNull();
        form.AllowClose = true;
    }

    /// <summary>
    /// Well inside the primary screen, and well over the minimum size at any scale.
    /// </summary>
    static Rectangle Inside()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        return new(area.X + area.Width / 8, area.Y + area.Height / 8, area.Width * 3 / 4, area.Height * 3 / 4);
    }

    static WindowPlacement PlacementOf(Rectangle bounds, bool maximized = false) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized);

    static MonitorForm Shown()
    {
        var form = new MonitorForm(ScreenBuilder.Title, 1000, 640)
        {
            Opacity = 0,
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        return form;
    }
}
