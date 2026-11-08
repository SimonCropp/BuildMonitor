/// <summary>
/// The tray's side of the socket called directly, so the MCP tools run against the real handler
/// over a state the test owns, with no port to bind or race.
/// </summary>
sealed class InProcessClient(MessageHandler handler) : IProtocolClient
{
    public Task<Response> Send(Message message, Cancel cancel) =>
        handler.Handle(message);
}
