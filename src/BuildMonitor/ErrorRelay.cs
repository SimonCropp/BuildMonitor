using ModelContextProtocol.Server;

/// <summary>
/// Passes a tool's or a prompt's failure on as what it says. The SDK sends the message of an
/// <see cref="McpException"/> alone, and replaces any other with "An error occurred invoking", to
/// keep whatever an exception carries off the wire. Every failure the tools and prompts raise is a
/// sentence written for the reader, from the tray or about it: "No build with key …", "That build
/// cannot be retried", that no tray is listening. Replaced, an assistant handed a mistyped key or a
/// running build to retry was told only that something went wrong, and guessed, or gave up; and a
/// user running triage with the tray closed was told the same.
/// <para>
/// Only those two types are passed on. They are what <see cref="MonitorTools"/> and
/// <see cref="ProtocolClient"/> throw with a message of their own; anything else is a fault, whose
/// message was written for a log, and keeps the SDK's.
/// </para>
/// </summary>
static class ErrorRelay
{
    /// <summary>
    /// For tool calls and prompts, the two kinds of request the server handles that can fail.
    /// </summary>
    public static void AddTo(McpRequestFilters filters)
    {
        filters.CallToolFilters.Add(Relay);
        filters.GetPromptFilters.Add(Relay);
    }

    public static McpRequestHandler<TParams, TResult> Relay<TParams, TResult>(McpRequestHandler<TParams, TResult> handler) =>
        async (request, cancel) =>
        {
            try
            {
                return await handler(request, cancel);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TrayUnreachableException)
            {
                throw new McpException(exception.Message, exception);
            }
        };
}
