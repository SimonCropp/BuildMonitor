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
    Action<Connection, AuthMethod, Guid> SignIn,
    Action<Guid> CancelSignIn,
    // The draft connection and the token typed for it, or null to use the stored one.
    Action<Connection, string?> Test,
    Action<string, string> StoreSecret,
    Action<string> DeleteSecret,
    Action OpenLogs,
    Action RaiseIssue,
    Action Update,
    Action<bool> SetRunAtLogin)
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
        (_, _, _) => throw new InvalidOperationException("SignIn"),
        _ => throw new InvalidOperationException("CancelSignIn"),
        (_, _) => throw new InvalidOperationException("Test"),
        (_, _) => throw new InvalidOperationException("StoreSecret"),
        _ => throw new InvalidOperationException("DeleteSecret"),
        () => throw new InvalidOperationException("OpenLogs"),
        () => throw new InvalidOperationException("RaiseIssue"),
        () => throw new InvalidOperationException("Update"),
        _ => throw new InvalidOperationException("SetRunAtLogin"));
}
