/// <summary>
/// One MCP server: the shim running <c>buildmonitor mcp</c> for an AI client. Found as a process
/// rather than counted in the session, because a client's server opens a socket per request and
/// closes it again, so there is nothing connected for the tray to know about between them.
/// </summary>
record McpServer(int ProcessId, DateTimeOffset Started);
