/// <summary>
/// The <see cref="MonitorActions"/> the app runs with. Anything slow goes to the thread pool
/// and reports back through the host; the applier that asked has long since returned.
/// </summary>
static class RealActions
{
    public static MonitorActions Create(SessionHost host, Poller poller, ISecretStore secrets, SignInCoordinator signIn, IRunAtLogin runAtLogin, Action exit) =>
        new(
            OpenUrl: LinkLauncher.OpenUrl,
            SaveSettings: settings =>
            {
                poller.Sync(settings);
                Background(() => SettingsHelper.Write(settings), host, "Saving settings");
            },
            Refresh: poller.Refresh,
            Retry: build => Background(
                () => poller.Retry(build, Cancel.None),
                host,
                $"Retrying {build.PipelineName}",
                $"Retried {build.PipelineName} {build.RunNumberLabel()}".TrimEnd()),
            Cancel: build => Background(
                () => poller.Cancel(build, Cancel.None),
                host,
                $"Cancelling {build.PipelineName}",
                $"Cancelled {build.PipelineName} {build.RunNumberLabel()}".TrimEnd()),
            SignIn: signIn.Start,
            CancelSignIn: signIn.Abandon,
            Test: (connection, token) => _ = Task.Run(async () =>
            {
                try
                {
                    var result = await poller.Test(connection, token, Cancel.None);
                    host.Mutate(_ => result.Ok
                        ? MonitorSession.SetFormMessage(_, result.Message)
                        : MonitorSession.SetFormError(_, result.Message));
                }
                catch (Exception exception)
                {
                    host.Mutate(_ => MonitorSession.SetFormError(_, exception.Message));
                }
            }),
            StoreSecret: secrets.Write,
            DeleteSecret: secrets.Delete,
            OpenLogs: Logging.OpenDirectory,
            RaiseIssue: IssueLauncher.Launch,
            Update: () => Updater.Run(exit),
            SetRunAtLogin: enabled =>
            {
                try
                {
                    runAtLogin.Set(enabled);
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Could not change run at login");
                    host.Mutate(_ => MonitorSession.SetStatus(_, $"Run at startup failed: {exception.Message}"));
                }
            });

    static void Background(Func<Task> work, SessionHost host, string what, string? done = null) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
                if (done is not null)
                {
                    host.Mutate(_ => MonitorSession.SetStatus(_, done));
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception, "{What} failed", what);
                host.Mutate(_ => MonitorSession.SetStatus(_, $"{what} failed: {exception.Message}"));
            }
        });
}
