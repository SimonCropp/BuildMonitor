/// <summary>
/// What a Claude Code session that loaded BuildMonitor as a channel is told without asking: a
/// pipeline's own build failing, and a build it asked to have watched finishing. Without it an
/// assistant learnt of either only by asking, and an idle session never asks.
/// <para>
/// Everything is read from the tray, as the tools read it, so the CI services see no more requests
/// for a session watching than for one that is not.
/// </para>
/// </summary>
sealed class ChannelFeed(MonitorTools tools, Func<ChannelEvent, Cancel, Task> send) : IDisposable
{
    /// <summary>
    /// How long a watch lasts before it gives up, saying so: longer than any build should take, and
    /// short enough that a watch on a build stuck in a queue does not outlive the day.
    /// </summary>
    public static TimeSpan WatchTimeout { get; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How often the tray is asked what is failing. It answers from what it holds, and polls the CI
    /// services on its own schedule, so looking more often would mostly read the same poll again.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);

    FailureWatch failures = new();
    CancelSource stopping = new();

    /// <summary>
    /// Looks for new failures until cancelled. A look the tray cannot answer, as when it is closed,
    /// is skipped rather than ending the watch: the tray may be started again.
    /// </summary>
    public async Task WatchFailures(Cancel cancel)
    {
        while (true)
        {
            try
            {
                await Look(cancel);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TrayUnreachableException)
            {
            }

            await Task.Delay(Interval, cancel);
        }
    }

    /// <summary>
    /// One look: the failures that are news, each sent as an event.
    /// </summary>
    public async Task Look(Cancel cancel)
    {
        var connections = await tools.ListConnections(cancel);
        var failing = await tools.ListFailing(null, cancel);
        foreach (var build in failures.Next(failing, connections))
        {
            await send(ChannelText.Failed(build), cancel);
        }
    }

    /// <summary>
    /// Starts watching a build in the background and returns the run it is watching, once the key
    /// has been found, so a mistyped one fails the call rather than an event an hour later. The
    /// watch is <see cref="MonitorTools.WaitForBuild"/> with nobody waiting on it, and its answer is
    /// sent as an event.
    /// </summary>
    public async Task<BuildDto> Watch(string key, bool nextRun, WaitScope scope, Cancel cancel)
    {
        var build = await tools.Current(key, cancel);
        _ = Run(key, nextRun, scope);
        return build;
    }

    async Task Run(string key, bool nextRun, WaitScope scope)
    {
        // Off the caller's call before the first look: a tray that answers at once and a wait that
        // does not pause would otherwise keep the call from returning until the build finished.
        await Task.Yield();
        var cancel = stopping.Token;
        ChannelEvent result;
        try
        {
            var wait = await tools.WaitForBuild(key, nextRun, scope, WatchTimeout, null, cancel);
            result = ChannelText.Finished(wait, WatchTimeout);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or TrayUnreachableException)
        {
            result = ChannelText.WatchFailed(key, exception.Message);
        }

        try
        {
            await send(result, cancel);
        }
        // The session it was for has gone, and there is nobody left to tell.
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }
}
