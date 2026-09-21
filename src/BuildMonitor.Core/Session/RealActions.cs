/// <summary>
/// The <see cref="MonitorActions"/> the app runs with. Anything slow goes to the thread pool
/// and reports back through the host; the applier that asked has long since returned.
/// </summary>
static class RealActions
{
    public static MonitorActions Create(SessionHost host, Poller poller, LocalRepoWatcher repos, ArtifactStore artifacts, ISecretStore secrets, SignInCoordinator signIn, IRunAtLogin runAtLogin) =>
        new(
            OpenUrl: LinkLauncher.OpenUrl,
            SaveSettings: settings =>
            {
                poller.Sync(settings);
                repos.Sync(settings.CodeDirectory);
                Background(() => SettingsHelper.Write(settings), host, "Saving settings");
            },
            Refresh: poller.Refresh,
            Retry: build => Background(
                () => poller.Retry(build, Cancel.None),
                host,
                $"Retrying {build.PipelineName}",
                $"Retried {build.PipelineName} {build.RunNumberLabel()}".TrimEnd(),
                build.ConnectionId),
            Cancel: build => Background(
                () => poller.Cancel(build, Cancel.None),
                host,
                $"Cancelling {build.PipelineName}",
                $"Cancelled {build.PipelineName} {build.RunNumberLabel()}".TrimEnd(),
                build.ConnectionId),
            RunNext: build => Background(
                () => poller.RunNext(build, Cancel.None),
                host,
                $"Moving {build.PipelineName} to the front of the queue",
                // Without the run number the other two carry: a build still in the queue often has
                // none yet, since several of the services only number a run once it starts.
                $"Moved {build.PipelineName} to the front of the queue",
                build.ConnectionId),
            CopyLog: build => _ = Task.Run(async () =>
            {
                var name = $"{build.PipelineName} {build.RunNumberLabel()}".TrimEnd();
                try
                {
                    var log = await poller.FetchLog(build, Cancel.None);
                    host.Mutate(_ => log.Length == 0
                        ? MonitorSession.SetStatus(_, $"{name} has no log to copy")
                        : MonitorSession.Copy(_, log, $"Copied the log of {name}"));
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Copying the log of {Build} failed", name);
                    host.Mutate(_ => MonitorSession.SetStatus(_, $"Copying the log of {name} failed: {exception.Message}"));
                }
            }),
            Triage: build => _ = Task.Run(async () =>
            {
                var name = $"{build.PipelineName} {build.RunNumberLabel()}".TrimEnd();
                try
                {
                    // Before the download rather than after it, which is when freeing the disk
                    // actually helps, and on the task that already catches and logs.
                    artifacts.Sweep();
                    // Through the same projection the MCP tools read, so the prompt on the clipboard
                    // and the one an assistant composes describe a build identically.
                    if (Snapshot.Find(host.State, build.Key, DateTimeOffset.UtcNow) is not { } dto)
                    {
                        host.Mutate(_ => MonitorSession.SetStatus(_, $"{name} is no longer in the build list"));
                        return;
                    }

                    var files = await ArtifactCollector.Collect(artifacts, poller, build, Cancel.None);
                    // fix is false: this text lands in whatever session the user pastes it into, and
                    // authorising edits there is not this action's call to make.
                    host.Mutate(_ => MonitorSession.Copy(_, TriagePrompt.One(dto, files, fix: false), Collected(name, files)));
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Collecting {Build} for triage failed", name);
                    host.Mutate(_ => MonitorSession.SetStatus(_, ActionFailure.Describe(_, build.ConnectionId, $"Triaging {name}", exception)));
                }
            }),
            SignIn: signIn.Start,
            CancelSignIn: signIn.Abandon,
            Test: (connection, token) => _ = Task.Run(async () =>
            {
                try
                {
                    var result = await poller.Test(connection, token, Cancel.None);
                    var message = result.Describe(ProviderDescriptors.Get(connection.ProviderId));
                    host.Mutate(_ => result.Ok
                        ? MonitorSession.SetFormMessage(_, message)
                        : MonitorSession.SetFormError(_, new(message)));
                }
                catch (Exception exception)
                {
                    host.Mutate(_ => MonitorSession.SetFormError(_, new(exception.Message)));
                }
            }),
            StoreSecret: secrets.Write,
            DeleteSecret: secrets.Delete,
            OpenLogs: Logging.OpenDirectory,
            OpenDirectory: RevealFile.OpenDirectory,
            RaiseIssue: IssueLauncher.Launch,
            RunningServers: McpServers.Find,
            Update: Updater.Start,
            SetRunAtLogin: enabled =>
            {
                try
                {
                    runAtLogin.Set(enabled);
                    return null;
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Could not change run at login");
                    return $"Run at startup failed: {exception.Message}";
                }
            });

    /// <summary>
    /// What the status line says once a triage lands. It names the directory because this is the
    /// only place the user, as opposed to the assistant reading the prompt, finds out where the
    /// files went.
    /// </summary>
    static string Collected(string name, TriageFilesDto triage)
    {
        var copied = $"Copied a triage prompt for {name}";
        var files = triage.Files;
        if (files.Count == 0)
        {
            return $"{copied}: it published no artifacts and has no log";
        }

        if (files is [ArtifactCollector.LogName])
        {
            return $"{copied}: the log only, in {triage.Directory}";
        }

        return $"{copied}: {files.Count} files in {triage.Directory}";
    }

    static void Background(Func<Task> work, SessionHost host, string what, string? done = null, string? connectionId = null) =>
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
                host.Mutate(_ => MonitorSession.SetStatus(_, ActionFailure.Describe(_, connectionId, what, exception)));
            }
        });
}
