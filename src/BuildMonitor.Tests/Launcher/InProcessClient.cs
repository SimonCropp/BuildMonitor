/// <summary>
/// The tray's side of the socket called directly, so the MCP tools run against the real handler
/// over a state the test owns, with no port to bind or race. Records what was sent, and
/// <paramref name="answer"/>, where it answers, stands in for the handler, as an older tray that
/// does not know a verb.
/// </summary>
sealed class InProcessClient(MessageHandler handler, Func<Message, Response?>? answer = null) : IProtocolClient
{
    public List<Message> Sent { get; } = [];

    public Task<Response> Send(Message message, Cancel cancel)
    {
        Sent.Add(message);
        if (answer?.Invoke(message) is { } answered)
        {
            return Task.FromResult(answered);
        }

        return handler.Handle(message);
    }
}
