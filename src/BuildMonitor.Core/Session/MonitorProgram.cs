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
        if (hidden)
        {
            return MonitorSession.Hide(state);
        }

        return state;
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
        if (UpdateOutcome.Take(AppPaths.UpdateOutcome) is { } outcome)
        {
            host.Mutate(_ => MonitorSession.Notify(_, outcome));
        }

        var secrets = new CachingSecretStore(SecretStores.ForPlatform(AppPaths.Secrets));
        var history = DurationHistory.Load(AppPaths.History);
        var handler = new RedirectingHandler(
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
                // Followed by RedirectingHandler instead, which carries the credential to a host of
                // the same service rather than dropping it at every host boundary. See its summary.
                AllowAutoRedirect = false
            });
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
        // One store for the chip and the protocol alike: its record of what is being written is
        // per instance, and a second one could sweep away a bundle the first was still filling.
        var artifacts = new ArtifactStore();
        var actions = RealActions.Create(host, poller, repos, artifacts, secrets, signIn, runAtLogin);
        poller.Start();
        // Scans off the loop's thread, so a code directory on a slow or absent network share
        // delays the checkouts being found rather than the window appearing.
        repos.Sync(settings.CodeDirectory);
        // Off the loop's thread for the same reason, and once at startup because a triage is the
        // only thing that fills the directory and the only thing that has to find it small.
        // ReSharper disable once MethodSupportsCancellation
        _ = Task.Run(artifacts.Sweep);
        var listening = server.Listen(new MessageHandler(host, poller, LinkLauncher.OpenUrl, windowCommands.Enqueue, artifacts).Handle, cancel.Token);

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
        var clipboard = new ClipboardPump();
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
                    // The state follows the window, as it does when the tray shows or hides it: the
                    // screen of a hidden window has no rows, and the clock does not tick it.
                    case WindowCommand.Show:
                        host.Mutate(MonitorSession.Show);
                        window.SetHidden(false);
                        window.Focus();
                        break;
                    case WindowCommand.Hide:
                        host.Mutate(MonitorSession.Hide);
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
                // Not cleared before the window is asked, unlike the notification: see
                // ClipboardPump, which owns the clearing and the few frames of retry.
                clipboard.Push(host, window, text);
            }

            if (!window.Present(screen))
            {
                return;
            }

            var input = window.Poll();
            if (tray is not null)
            {
                var trayInput = tray.Poll();
                if (trayInput.ClickedItem is not null ||
                    trayInput.IconClicked ||
                    trayInput.ClickedNotification is not null)
                {
                    input = input with
                    {
                        TrayItem = trayInput.ClickedItem,
                        TrayIconClicked = trayInput.IconClicked,
                        ClickedNotification = trayInput.ClickedNotification
                    };
                }
            }

            input = input with {At = DateTimeOffset.UtcNow};
            host.Mutate(_ => InputApplier.Apply(_, input, actions, window));
            if (host.State.Hidden)
            {
                Thread.Sleep(HiddenFrame);
            }
        }
    }
}
