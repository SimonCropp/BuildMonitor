/// <summary>
/// One tray menu entry. <see cref="Id"/> is what a head reports back when it is clicked; the
/// fixed items have the well known ids in <see cref="TrayMenu"/>, and per build items carry the
/// build key and the action.
/// </summary>
record TrayMenuItem(
    string Id,
    string Label,
    bool Enabled = true,
    bool Separator = false,
    string? IconName = null,
    IReadOnlyList<TrayMenuItem>? Children = null);
