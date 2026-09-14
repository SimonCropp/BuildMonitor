/// <summary>
/// One tray menu entry. <see cref="Id"/> is what a head reports back when it is clicked, one of
/// the well known ids in <see cref="TrayMenu"/>.
/// </summary>
record TrayMenuItem(
    string Id,
    string Label,
    bool Enabled = true,
    bool Separator = false,
    string? IconName = null);
