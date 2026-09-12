/// <summary>
/// The Windows tray icon. The menu is rebuilt from the last <see cref="TrayModel"/> each time it
/// opens, which is when its contents matter, rather than on every frame.
/// </summary>
sealed class NotifyIconTray : ITray
{
    readonly NotifyIcon icon;
    readonly ContextMenuStrip menu = new();
    TrayModel? model;
    TrayIconKind? shown;
    string? clicked;
    bool iconClicked;

    NotifyIconTray()
    {
        MenuTheme.Apply(menu);
        menu.Opening += (_, _) => Rebuild();
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

    void Rebuild()
    {
        foreach (ToolStripItem item in menu.Items)
        {
            item.Dispose();
        }

        menu.Items.Clear();
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
        if (item.Children is { Count: > 0 })
        {
            foreach (var child in item.Children)
            {
                menuItem.DropDownItems.Add(Create(child));
            }

            MenuTheme.Apply(menuItem.DropDown);
        }

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
