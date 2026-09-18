/// <summary>
/// Every side effect the applier can ask for, as delegates, so the transitions stay pure and a
/// test can record what would have happened. Anything asynchronous re-enters through
/// <see cref="SessionHost.Mutate"/> when it completes; the applier never waits.
/// </summary>
record MonitorActions(
    Action<string> OpenUrl,
    Action<Settings> SaveSettings,
    // A connection id, or null for every connection.
    Action<string?> Refresh,
    Action<Build> Retry,
    Action<Build> Cancel,
    // Fetches a failed build's log, which arrives on the clipboard through the state.
    Action<Build> CopyLog,
    Action<Connection, AuthMethod, Guid> SignIn,
    Action<Guid> CancelSignIn,
    // The draft connection and the token typed for it, or null to use the stored one.
    Action<Connection, string?> Test,
    Action<string, string> StoreSecret,
    Action<string> DeleteSecret,
    Action OpenLogs,
    // Opens a local checkout in the desktop's file manager.
    Action<string> OpenDirectory,
    Action RaiseIssue,
    // What is running that an update would take down, for the page that asks before it does.
    Func<McpServers> RunningServers,
    // Starts the update. Exiting is the applier's to return, not this one's to do: the tray has to
    // be gone before the update can replace its files, and a quit swapped in from in here is undone
    // by the applier returning the state it had already computed.
    Action Update,
    // Why it could not be changed, or null when it was.
    Func<bool, string?> SetRunAtLogin)
{
    /// <summary>
    /// For tests of transitions that never reach an action. Anything that does throws, which is
    /// a test finding out it needed a recording double.
    /// </summary>
    public static readonly MonitorActions None = new(
        _ => throw new InvalidOperationException("OpenUrl"),
        _ => throw new InvalidOperationException("SaveSettings"),
        _ => throw new InvalidOperationException("Refresh"),
        _ => throw new InvalidOperationException("Retry"),
        _ => throw new InvalidOperationException("Cancel"),
        _ => throw new InvalidOperationException("CopyLog"),
        (_, _, _) => throw new InvalidOperationException("SignIn"),
        _ => throw new InvalidOperationException("CancelSignIn"),
        (_, _) => throw new InvalidOperationException("Test"),
        (_, _) => throw new InvalidOperationException("StoreSecret"),
        _ => throw new InvalidOperationException("DeleteSecret"),
        () => throw new InvalidOperationException("OpenLogs"),
        _ => throw new InvalidOperationException("OpenDirectory"),
        () => throw new InvalidOperationException("RaiseIssue"),
        () => throw new InvalidOperationException("RunningServers"),
        () => throw new InvalidOperationException("Update"),
        _ => throw new InvalidOperationException("SetRunAtLogin"));
}
