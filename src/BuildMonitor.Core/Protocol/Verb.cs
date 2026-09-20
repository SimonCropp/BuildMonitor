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
    // Every run of one pipeline, which the rows collapse: a build or a pipeline key.
    Runs,
    // Poll now: a connection id, or everything.
    Refresh,
    Retry,
    Cancel,
    // Move a queued build to the front of its service's queue.
    RunNext,
    Connections,
    // Every pipeline being monitored, whether or not it has a recent build.
    Pipelines,
    Summary,
    // Open a build's link in the browser: key plus which of build, branch, pr.
    Open,
    // The log of a build: key plus how many lines to keep of each section.
    Log,
    // Download a build's artifacts and its log to a local directory: key. Answers with the paths,
    // never with the bytes.
    Triage
}
