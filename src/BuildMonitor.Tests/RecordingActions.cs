/// <summary>
/// A <see cref="MonitorActions"/> that writes down what it was asked, for asserting on.
/// </summary>
class RecordingActions
{
    public List<string> Calls { get; } = [];
    public Settings? SavedSettings { get; private set; }

    /// <summary>
    /// What the update page is told is running. Nothing by default: listing the machine's real
    /// processes would make a test depend on whatever else happens to be up.
    /// </summary>
    public McpServers Servers { get; set; } = McpServers.None;

    /// <summary>
    /// Why run at login could not be set, or null for the usual case where it could.
    /// </summary>
    public string? RunAtLoginError { get; set; }

    public MonitorActions Actions =>
        new(
            _ => Calls.Add($"OpenUrl {_}"),
            _ =>
            {
                SavedSettings = _;
                Calls.Add("SaveSettings");
            },
            _ => Calls.Add($"Refresh {_ ?? "all"}"),
            _ => Calls.Add($"Retry {_.Key}"),
            _ => Calls.Add($"Cancel {_.Key}"),
            _ => Calls.Add($"CopyLog {_.Key}"),
            _ => Calls.Add($"Triage {_.Key}"),
            (connection, method, _) => Calls.Add($"SignIn {connection.ProviderId} {method}"),
            _ => Calls.Add("CancelSignIn"),
            (connection, token) => Calls.Add($"Test {connection.ProviderId} token={token is not null}"),
            (key, _) => Calls.Add($"StoreSecret {key}"),
            _ => Calls.Add($"DeleteSecret {_}"),
            () => Calls.Add("OpenLogs"),
            _ => Calls.Add($"OpenDirectory {_}"),
            () => Calls.Add("RaiseIssue"),
            () =>
            {
                Calls.Add("RunningServers");
                return Servers;
            },
            () => Calls.Add("Update"),
            _ =>
            {
                Calls.Add($"SetRunAtLogin {_}");
                return RunAtLoginError;
            });
}
