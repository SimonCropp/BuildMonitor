/// <summary>
/// The Windows tray icon. The menu is rebuilt from the last <see cref="TrayModel"/> each time it
/// opens, which is when its contents matter, rather than on every frame.
/// </summary>
sealed class NotifyIconTray : ITray
{
    NotifyIcon icon;
    ContextMenuStrip menu = new();
    TrayModel? model;
    TrayIconKind? shown;
    string? clicked;
    bool iconClicked;

    NotifyIconTray()
    {
        MenuTheme.Apply(menu);
        // An empty ContextMenuStrip marks Opening cancelled before raising it, and the menu is
        // empty until the first Rebuild, so without clearing that the first right click shows
        // nothing.
        menu.Opening += (_, arguments) =>
        {
            Rebuild();
            arguments.Cancel = menu.Items.Count == 0;
        };
        icon = new()
        {
            Text = ScreenBuilder.Title,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.MouseClick += (_, arguments) =>
        {
            if (arguments.Button == MouseButtons.Left)
            {
                iconClicked = true;
            }
        };
        icon.DoubleClick += (_, _) => iconClicked = true;
        // After the icon is added, since that is what makes Windows record it.
        if (Environment.ProcessPath is { } processPath)
        {
            NotifyIconSettings.Apply(processPath);
        }
    }

    public static ITray? Open(out string? error)
    {
        error = null;
        try
        {
            return new NotifyIconTray();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    public void Apply(TrayModel next)
    {
        model = next;
        if (shown != next.Icon)
        {
            shown = next.Icon;
            icon.Icon = Icons.Tray(next.Icon);
        }

        // NotifyIcon.Text is limited to 127 characters.
        var tooltip = next.Tooltip.Length > 127 ? next.Tooltip[..127] : next.Tooltip;
        if (icon.Text != tooltip)
        {
            icon.Text = tooltip;
        }
    }

    public void Notify(Notification notification) =>
        icon.ShowBalloonTip(5000, notification.Title, notification.Message, ToolTipIcon.Error);

    void Rebuild()
    {
        // A snapshot: disposing an item removes it from menu.Items, which would break the enumerator.
        foreach (var item in menu.Items.Cast<ToolStripItem>().ToList())
        {
            item.Dispose();
        }

        menu.Items.Clear();
        MenuTheme.Apply(menu);
        if (model is null)
        {
            return;
        }

        foreach (var item in model.Items)
        {
            menu.Items.Add(Create(item));
        }
    }

    ToolStripItem Create(TrayMenuItem item)
    {
        if (item.Separator)
        {
            return new ToolStripSeparator();
        }

        var menuItem = new ToolStripMenuItem(item.Label)
        {
            Enabled = item.Enabled,
            ForeColor = item.Enabled ? Palette.Text : Palette.Dim,
            Image = item.IconName is null ? null : Icons.Glyph(item.IconName),
            Tag = item.Id
        };
        menuItem.Click += (_, _) => clicked = item.Id;
        return menuItem;
    }

    public TrayInput Poll()
    {
        var input = new TrayInput(clicked, iconClicked);
        clicked = null;
        iconClicked = false;
        return input;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
    }
}
