/// <summary>
/// The Linux tray: a StatusNotifierItem on the session bus, with its menu exported as a
/// com.canonical.dbusmenu, which is what KDE, Cinnamon and XFCE show and what GNOME shows once
/// the AppIndicator extension is installed. Pure managed: no GTK and no libappindicator, which
/// would need a GTK main loop on this thread.
/// </summary>
sealed class SniTray : ITray
{
    public const string WatcherName = "org.kde.StatusNotifierWatcher";
    public static TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    DBusConnection connection;
    string busName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
    ConcurrentQueue<TrayInput> events = new();
    StatusNotifierItemHandler item;
    DbusMenuHandler menu;
    TrayIconKind? shownIcon;
    string? shownTooltip;

    SniTray(string address)
    {
        connection = new(address);
        item = new(this);
        menu = new(this);
    }

    public static ITray? Open(out string? error)
    {
        error = null;
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            error = "No session bus (DBUS_SESSION_BUS_ADDRESS is not set), so there is no tray to put an icon in.";
            return null;
        }

        var tray = new SniTray(address);
        try
        {
            var connect = tray.Connect();
            if (!connect.Wait(ConnectTimeout))
            {
                error = "The session bus did not answer.";
                tray.Dispose();
                return null;
            }

            if (!connect.Result)
            {
                error = $"Nothing on the session bus owns {WatcherName}. GNOME needs the AppIndicator extension for a tray.";
                tray.Dispose();
                return null;
            }

            return tray;
        }
        catch (Exception exception)
        {
            error = exception.InnerException?.Message ?? exception.Message;
            tray.Dispose();
            return null;
        }
    }

    async Task<bool> Connect()
    {
        await connection.ConnectAsync();
        connection.AddMethodHandler(item);
        connection.AddMethodHandler(menu);
        await connection.RequestNameAsync(busName, RequestNameOptions.None);
        if (!await HasOwner(WatcherName))
        {
            return false;
        }

        await Register();
        // A watcher that restarts, as the panel does when it crashes or is reconfigured, needs
        // the item registered again.
        await connection.WatchSignalAsync(
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus",
            "org.freedesktop.DBus",
            "NameOwnerChanged",
            ReadNameOwnerChanged,
            OnNameOwnerChanged,
            ObserverFlags.None,
            false);
        return true;
    }

    static (string Name, string NewOwner) ReadNameOwnerChanged(Tmds.DBus.Protocol.Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var name = reader.ReadString();
        reader.ReadString();
        var newOwner = reader.ReadString();
        return (name, newOwner);
    }

    void OnNameOwnerChanged(Notification<(string Name, string NewOwner)> notification)
    {
        if (notification.Exception is { } exception)
        {
            Log.Warning(exception, "NameOwnerChanged");
            return;
        }

        if (notification is {HasValue: true, Value.Name: WatcherName} &&
            notification.Value.NewOwner.Length > 0)
        {
            _ = Register();
        }
    }

    Task<bool> HasOwner(string name)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s");
        writer.WriteString(name);
        var message = writer.CreateMessage();
        writer.Dispose();
        return connection.CallMethodAsync(message, ReadBool);
    }

    static bool ReadBool(Tmds.DBus.Protocol.Message message, object? state) =>
        message.GetBodyReader().ReadBool();

    async Task Register()
    {
        try
        {
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(WatcherName, "/StatusNotifierWatcher", WatcherName, "RegisterStatusNotifierItem", "s");
            writer.WriteString(busName);
            var message = writer.CreateMessage();
            writer.Dispose();
            await connection.CallMethodAsync(message);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Registering the tray item failed");
        }
    }

    public void Apply(TrayModel model)
    {
        if (shownIcon != model.Icon)
        {
            shownIcon = model.Icon;
            item.Icon = model.Icon;
            Signal(StatusNotifierItemHandler.ObjectPath, StatusNotifierItemHandler.Interface, "NewIcon", "", null);
            var status = item.StatusText;
            Signal(StatusNotifierItemHandler.ObjectPath, StatusNotifierItemHandler.Interface, "NewStatus", "s", status);
        }

        if (shownTooltip != model.Tooltip)
        {
            shownTooltip = model.Tooltip;
            item.Tooltip = model.Tooltip;
            Signal(StatusNotifierItemHandler.ObjectPath, StatusNotifierItemHandler.Interface, "NewToolTip", "", null);
        }

        if (menu.Update(model.Items))
        {
            Signal(DbusMenuHandler.ObjectPath, DbusMenuHandler.Interface, "LayoutUpdated", "ui", menu.Revision);
        }
    }

    /// <summary>
    /// The three signal shapes used here: no body, one string, or a revision and the root id.
    /// </summary>
    void Signal(string path, string interfaceName, string member, string signature, object? argument)
    {
        try
        {
            var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null, path, interfaceName, member, signature);
            switch (argument)
            {
                case string text:
                    writer.WriteString(text);
                    break;
                case uint revision:
                    writer.WriteUInt32(revision);
                    writer.WriteInt32(0);
                    break;
            }

            var message = writer.CreateMessage();
            writer.Dispose();
            connection.TrySendMessage(message);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Signal {Member}", member);
        }
    }

    /// <summary>
    /// notify-send speaks org.freedesktop.Notifications for us and is installed wherever a
    /// notification daemon is. Arguments go as a list, so a title with a quote in it is safe.
    /// </summary>
    public void Notify(Notification notification) =>
        ProcessRunner.Run(
            "notify-send",
            ["--app-name=BuildMonitor", "--urgency=normal", notification.Title, notification.Message],
            timeout: TimeSpan.FromSeconds(5));

    public void IconClicked() =>
        events.Enqueue(new(IconClicked: true));

    public void ItemClicked(string id) =>
        events.Enqueue(new(ClickedItem: id));

    public TrayInput Poll()
    {
        if (!events.TryDequeue(out var first))
        {
            return new();
        }

        // Fold everything queued since the last frame into one report; a click is a click.
        var result = first;
        while (events.TryDequeue(out var next))
        {
            result = new(next.ClickedItem ?? result.ClickedItem, result.IconClicked || next.IconClicked);
        }

        return result;
    }

    public void Dispose() =>
        connection.Dispose();
}
