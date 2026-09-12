/// <summary>
/// What the launcher and the MCP server can ask the running tray.
/// </summary>
enum Verb
{
    Ping,
    Show,
    Hide,
    Quit,
    // Every row, as JSON.
    List,
    // One build by key.
    Get,
    // Poll now: a connection id, or everything.
    Refresh,
    Retry,
    Cancel,
    Connections,
    Summary,
    // Open a build's link in the browser: key plus which of build, branch, pr.
    Open
}
