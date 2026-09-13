/// <summary>
/// The launcher's verbs. No arguments starts the tray, or shows the one that is running.
/// </summary>
enum CommandKindLauncher
{
    Start,
    Show,
    Hide,
    Quit,
    Refresh,
    Status,
    Mcp,
    Version,
    Help,
    Unknown
}