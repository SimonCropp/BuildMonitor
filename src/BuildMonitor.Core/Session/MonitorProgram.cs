/// <summary>
/// The app, for every head. A head is a Main that picks a window and a tray; everything else
/// is here: settings, the single instance gate, the poller, the socket, and the frame loop.
/// </summary>
static class MonitorProgram
{
    public const int WindowWidth = 1000;
    public const int WindowHeight = 640;

    /// <summary>
    /// Starts the tray with no window, whatever ShowWindowAtStart says. That setting answers "the
    /// user started BuildMonitor, should a window appear", and a tray started as a side effect of
    /// something else was not started by the user: the MCP server needs one running to answer a
    /// question, and taking the screen to do it is not what was asked for.
    /// </summary>
    public const string HiddenArgument = "--hidden";

    /// <summary>
    /// How long the loop sleeps between frames when the window is hidden. Nothing is drawn, so
    /// the only thing to keep up with is the tray, which does not need sixty a second.
    /// </summary>
    public static readonly TimeSpan HiddenFrame = TimeSpan.FromMilliseconds(100);
    /// <summary>
    /// What the run starts from. <paramref name="hidden"/> wins over ShowWindowAtStart, and does it
    /// on the state rather than on the settings: the setting is the user's, and one written back
    /// here would be saved for good by the next save of the options page.
    /// </summary>
    public static SessionState StartState(Settings settings, bool hidden)
    {
        var state = SessionState.Start(settings);
        return hidden ? MonitorSession.Hide(state) : state;
    }

    public static int Run(string[] args, OpenWindow openWindow, OpenTray openTray)
    {
        Logging.Init();
        Log.Information("BuildMonitor {Version} starting", VersionReader.VersionString);
        Settings settings;
        try
        {
            settings = SettingsHelper.Read();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed to read settings");
            IssueLauncher.LaunchForException($"Cannot start. Failed to read settings: {AppPaths.Settings}", exception);
            return 2;
        }

        var hidden = args.Contains(HiddenArgument);
        var port = Port.Resolve(settings);
        if (!LocalServer.TryBind(port, out var server))
        {
            // A tray started to answer a question races one the user started; whichever loses the
            // port must not then pull the winner's window up, which is the thing --hidden is for.
            if (hidden)
            {
                Log.Information("A tray already owns port {Port}. Leaving it as it is.", port);
                return 0;
            }

            // Another tray owns the port: hand it the show and leave.
            Log.Information("A tray already owns port {Port}. Asking it to show.", port);
            try
            {
                new ProtocolClient(port).Send(new(Verb.Show), Cancel.None).GetAwaiter().GetResult();
            }
            catch (TrayUnreachableException exception)
            {
                Log.Warning(exception, "Port {Port} is taken but nothing answered", port);
                return 3;
            }

            return 0;
        }

        using (server)
        {
            return RunOwned(args, settings, hidden, server, openWindow, openTray);
        }
    }

    static int RunOwned(string[] args, Settings settings, bool hidden, LocalServer server, OpenWindow openWindow, OpenTray openTray)
    {
        var host = new SessionHost(StartState(settings, hidden));
        // Read only by the tray that owns the port, so one started beside it and leaving at once
        // does not take the report with it.
        if (FailedUpdate.Take(AppPaths.FailedUpdate) is { } failedUpdate)
        {
            host.Mutate(_ => MonitorSession.Notify(_, failedUpdate));
        }

        var secrets = new CachingSecretStore(SecretStores.ForPlatform(AppPaths.Secrets));
        var history = DurationHistory.Load(AppPaths.History);
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };
        var windowCommands = new ConcurrentQueue<WindowCommand>();
        var signIn = new SignInCoordinator(host, secrets, handler);
        var runAtLogin = RunAtLogin.ForPlatform();
        host.Mutate(_ => MonitorSession.ApplyMedians(_, history.Medians()));

        var tray = openTray(out var trayError);
        if (tray is null &&
            !hidden)
        {
            // With no icon the window is the only way back to it, so it opens whatever the setting
            // says. Except when this tray is only here to answer the socket: nothing was asked for
            // on screen, and `buildmonitor` still brings it up.
            Log.Warning("No tray: {Error}. Running with the window only.", trayError);
            host.Mutate(MonitorSession.Show);
        }

        var window = openWindow(ScreenBuilder.Title, WindowWidth, WindowHeight, host.State.Hidden, out var windowError);
        if (window is null)
        {
            Log.Fatal("Could not open a window: {Error}", windowError);
            Console.Error.WriteLine(windowError);
            tray?.Dispose();
            return 4;
        }

        using var cancel = new CancelSource();
        var poller = new Poller(host, secrets, history, handler, new(secrets, handler));
        using var repos = new LocalRepoWatcher(host);
        var actions = RealActions.Create(host, poller, repos, secrets, signIn, runAtLogin, () => host.Mutate(MonitorSession.Quit));
        poller.Start();
        // Scans off the loop's thread, so a code directory on a slow or absent network share
        // delays the checkouts being found rather than the window appearing.
        repos.Sync(settings.CodeDirectory);
        var listening = server.Listen(new MessageHandler(host, poller, LinkLauncher.OpenUrl, windowCommands.Enqueue).Handle, cancel.Token);

        try
        {
            using (tray)
            using (window)
            {
                Loop(host, window, tray, actions, windowCommands, args);
            }
        }
        finally
        {
            cancel.Cancel();
            try
            {
                poller.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
                listening.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            history.Save(AppPaths.History);
            Log.Information("BuildMonitor exiting");
            Log.CloseAndFlush();
        }

        return 0;
    }

    static void Loop(SessionHost host, IMonitorWindow window, ITray? tray, MonitorActions actions, ConcurrentQueue<WindowCommand> windowCommands, string[] args)
    {
        if (args.Contains("--options"))
        {
            host.Mutate(MonitorSession.OpenOptions);
        }

        var screens = new ScreenCache();
        while (true)
        {
            // Socket driven window changes are applied on this thread rather than the listener's,
            // because every head is single threaded.
            while (windowCommands.TryDequeue(out var command))
            {
                switch (command)
                {
                    case WindowCommand.Close:
                        return;
                    case WindowCommand.Focus:
                        window.Focus();
                        break;
                    case WindowCommand.Show:
                        window.SetHidden(false);
                        window.Focus();
                        break;
                    case WindowCommand.Hide:
                        window.SetHidden(true);
                        break;
                }
            }

            var state = host.State;
            if (state.Exit)
            {
                return;
            }

            var screen = screens.Get(state, DateTimeOffset.UtcNow, out var rebuilt);
            if (rebuilt)
            {
                tray?.Apply(screen.Tray);
            }

            if (screen.Notification is { } notification)
            {
                // Cleared before the tray is asked, so a tray that throws does not pop it every frame.
                host.Mutate(MonitorSession.ClearNotification);
                try
                {
                    tray?.Notify(notification);
                }
                catch (Exception exception)
                {
                    Log.Warning(exception, "Notification failed");
                }
            }

            if (state.Clipboard is { } text)
            {
                // Cleared before the window is asked, like the notification, so a clipboard that
                // throws is not asked again every frame.
                host.Mutate(_ => MonitorSession.Copied(_, text));
                window.SetClipboard(text);
            }

            if (!window.Present(screen))
            {
                return;
            }

            var input = window.Poll();
            if (tray is not null)
            {
                var trayInput = tray.Poll();
                if (trayInput.ClickedItem is not null || trayInput.IconClicked)
                {
                    input = input with { TrayItem = trayInput.ClickedItem, TrayIconClicked = trayInput.IconClicked };
                }
            }

            host.Mutate(_ => InputApplier.Apply(_, input, actions, window));
            if (host.State.Hidden)
            {
                Thread.Sleep(HiddenFrame);
            }
        }
    }
}
