/// <summary>
/// The Windows tray icon. The menu is rebuilt from the last <see cref="TrayModel"/> each time it
/// opens, which is when its contents matter, rather than on every frame: see
/// <see cref="MenuOnOpening"/>.
/// </summary>
sealed class NotifyIconTray : ITray
{
    NotifyIcon icon;
    ContextMenuStrip menu = new();
    TrayModel? model;
    TrayIconKind? shown;
    string? clicked;
    bool iconClicked;
    string? announced;
    string? clickedNotification;

    NotifyIconTray()
    {
        MenuTheme.Apply(menu);
        MenuOnOpening.Rebuild(menu, Build);
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
        icon.BalloonTipClicked += (_, _) => clickedNotification = announced ?? "";
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

        // NotifyIcon.Text throws past its limit. ScreenBuilder fits the tooltip to it already, so
        // this only guards against that ever changing.
        var limit = ScreenBuilder.TrayTooltipLimit;
        var tooltip = next.Tooltip.Length > limit ? next.Tooltip[..limit] : next.Tooltip;
        if (icon.Text != tooltip)
        {
            icon.Text = tooltip;
        }
    }

    /// <summary>
    /// The key is kept rather than passed through the click, because Windows raises
    /// BalloonTipClicked with nothing about which balloon was clicked, and a second failure can
    /// pop over the first before either is.
    /// </summary>
    public void Notify(Notification notification)
    {
        announced = notification.Key ?? "";
        var balloonIcon = notification.Kind == NotificationKind.Info ? ToolTipIcon.Info : ToolTipIcon.Error;
        icon.ShowBalloonTip(5000, notification.Title, notification.Message, balloonIcon);
    }

    void Build(ToolStripItemCollection items)
    {
        MenuTheme.Apply(menu);
        if (model is null)
        {
            return;
        }

        foreach (var item in model.Items)
        {
            items.Add(Create(item));
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
        var input = new TrayInput(clicked, iconClicked, clickedNotification);
        clicked = null;
        iconClicked = false;
        clickedNotification = null;
        return input;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
    }
}
