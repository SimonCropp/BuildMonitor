/// <summary>
/// Follows the desktop session's lock into <see cref="SessionState.Locked"/>, by asking
/// <see cref="ISessionLock"/> every few seconds off the loop's thread. While locked the schedule
/// polls nothing more often than <see cref="PollSchedule.LockedInterval"/>, rather than keep a
/// running build on the running interval all night for nobody. On unlock every connection is
/// refreshed, so the rows are right when first looked at rather than minutes later.
/// <para>
/// A machine that slept is refreshed too: its rows are as old as the sleep, and whether the
/// poller's delays counted the time asleep depends on the clock the platform's timers run on.
/// Waking normally goes through the lock screen, but not where locking on sleep is turned off.
/// </para>
/// </summary>
sealed class LockWatcher
{
    /// <summary>
    /// How often the lock is asked about. An unlock waits this long at most for its refresh.
    /// </summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A gap between two looks longer than this is the machine having slept, as a look is due
    /// every <see cref="Every"/>.
    /// </summary>
    public static readonly TimeSpan SleptAfter = TimeSpan.FromMinutes(1);

    SessionHost host;
    ISessionLock sessionLock;
    Action refresh;
    Func<DateTimeOffset> clock;
    DateTimeOffset? looked;
    // So a desktop that cannot answer logs it once rather than every few seconds.
    bool failing;

    public LockWatcher(SessionHost host, ISessionLock sessionLock, Action refresh, Func<DateTimeOffset>? clock = null)
    {
        this.host = host;
        this.sessionLock = sessionLock;
        this.refresh = refresh;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task Run(Cancel cancel) =>
        Task.Run(
            async () =>
            {
                try
                {
                    while (!cancel.IsCancellationRequested)
                    {
                        await Look(cancel);
                        await Task.Delay(Every, cancel);
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                }
            },
            Cancel.None);

    public async Task Look(Cancel cancel)
    {
        bool? locked;
        try
        {
            locked = await sessionLock.IsLocked(cancel);
            failing = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!failing)
            {
                Log.Warning(exception, "Asking whether the session is locked failed");
            }

            failing = true;
            locked = null;
        }

        Apply(locked, clock());
    }

    /// <summary>
    /// One answer, at <paramref name="now"/>. Null leaves the state as it is.
    /// </summary>
    public void Apply(bool? locked, DateTimeOffset now)
    {
        var gap = now - looked;
        looked = now;
        var wasLocked = host.State.Locked is not null;
        if (locked == true &&
            !wasLocked)
        {
            Log.Information("Session locked. Polling no more often than every five minutes.");
            host.Mutate(MonitorSession.Lock);
            return;
        }

        if (locked == false &&
            wasLocked)
        {
            Log.Information("Session unlocked. Refreshing.");
            host.Mutate(MonitorSession.Unlock);
            refresh();
            return;
        }

        if (gap > SleptAfter &&
            !wasLocked)
        {
            Log.Information("Nothing looked for {Gap}, as after a sleep. Refreshing.", gap);
            refresh();
        }
    }
}
