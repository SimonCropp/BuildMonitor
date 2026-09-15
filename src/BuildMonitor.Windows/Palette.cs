/// <summary>
/// The dark and light themes, read through one switch so every control and the owner drawn rows
/// agree. Controls that copy a colour at construction are rethemed by <see cref="MonitorForm"/>
/// when <see cref="Use"/> reports a change; menus take theirs each time they open.
/// </summary>
static class Palette
{
    public static bool Light { get; private set; }

    /// <summary>
    /// Returns true when the palette changed, so a caller rethemes only then.
    /// </summary>
    public static bool Use(Theme theme)
    {
        var light = theme switch
        {
            Theme.Light => true,
            Theme.Dark => false,
            _ => Application.SystemColorMode != SystemColorMode.Dark
        };
        if (light == Light)
        {
            return false;
        }

        Light = light;
        return true;
    }

    public static Color Background => Pick(Color.FromArgb(24, 24, 24), Color.FromArgb(250, 250, 250));
    public static Color Surface => Pick(Color.FromArgb(32, 32, 32), Color.FromArgb(240, 240, 240));
    public static Color SelectedRow => Pick(Color.FromArgb(44, 50, 66), Color.FromArgb(204, 222, 245));
    public static Color HoverRow => Pick(Color.FromArgb(36, 36, 40), Color.FromArgb(236, 238, 244));
    public static Color Text => Pick(Color.FromArgb(212, 212, 212), Color.FromArgb(32, 32, 32));
    public static Color Dim => Pick(Color.FromArgb(140, 140, 140), Color.FromArgb(110, 110, 110));
    public static Color Border => Pick(Color.FromArgb(56, 56, 56), Color.FromArgb(204, 204, 204));
    public static Color BarTrack => Pick(Color.FromArgb(58, 58, 58), Color.FromArgb(220, 220, 220));
    public static Color Chip => Pick(Color.FromArgb(52, 52, 56), Color.FromArgb(226, 226, 232));
    public static Color ChipText => Pick(Color.FromArgb(180, 200, 255), Color.FromArgb(0, 90, 180));
    public static Color RetryChip => Pick(Color.FromArgb(48, 82, 52), Color.FromArgb(200, 230, 204));
    public static Color CancelChip => Pick(Color.FromArgb(96, 52, 52), Color.FromArgb(244, 208, 208));
    public static Color Error => Pick(Color.FromArgb(233, 129, 129), Color.FromArgb(196, 43, 28));

    public static Color Status(BuildStatus status) =>
        status switch
        {
            BuildStatus.Queued => Pick(Color.FromArgb(150, 150, 150), Color.FromArgb(120, 120, 120)),
            BuildStatus.Running => Pick(Color.FromArgb(86, 156, 214), Color.FromArgb(0, 120, 212)),
            BuildStatus.Succeeded => Pick(Color.FromArgb(126, 214, 139), Color.FromArgb(16, 124, 16)),
            BuildStatus.Failed => Pick(Color.FromArgb(233, 129, 129), Color.FromArgb(196, 43, 28)),
            BuildStatus.Cancelled => Pick(Color.FromArgb(160, 160, 160), Color.FromArgb(120, 120, 120)),
            _ => Pick(Color.FromArgb(120, 120, 120), Color.FromArgb(140, 140, 140))
        };

    static Color Pick(Color dark, Color light)
    {
        if (Light)
        {
            return light;
        }

        return dark;
    }
}
