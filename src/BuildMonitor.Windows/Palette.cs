/// <summary>
/// The dark theme, as constants, so every control and the owner drawn rows agree.
/// </summary>
static class Palette
{
    public static readonly Color Background = Color.FromArgb(24, 24, 24);
    public static readonly Color Surface = Color.FromArgb(32, 32, 32);
    public static readonly Color HeaderRow = Color.FromArgb(38, 38, 38);
    public static readonly Color SelectedRow = Color.FromArgb(44, 50, 66);
    public static readonly Color HoverRow = Color.FromArgb(36, 36, 40);
    public static readonly Color Text = Color.FromArgb(212, 212, 212);
    public static readonly Color Dim = Color.FromArgb(140, 140, 140);
    public static readonly Color Border = Color.FromArgb(56, 56, 56);
    public static readonly Color BarTrack = Color.FromArgb(58, 58, 58);
    public static readonly Color Chip = Color.FromArgb(52, 52, 56);
    public static readonly Color ChipText = Color.FromArgb(180, 200, 255);
    public static readonly Color RetryChip = Color.FromArgb(48, 82, 52);
    public static readonly Color CancelChip = Color.FromArgb(96, 52, 52);
    public static readonly Color Error = Color.FromArgb(233, 129, 129);

    public static Color Status(BuildStatus status) =>
        status switch
        {
            BuildStatus.Queued => Color.FromArgb(150, 150, 150),
            BuildStatus.Running => Color.FromArgb(86, 156, 214),
            BuildStatus.Succeeded => Color.FromArgb(126, 214, 139),
            BuildStatus.Failed => Color.FromArgb(233, 129, 129),
            BuildStatus.Cancelled => Color.FromArgb(160, 160, 160),
            _ => Color.FromArgb(120, 120, 120)
        };
}
