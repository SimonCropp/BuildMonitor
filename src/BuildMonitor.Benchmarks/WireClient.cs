/// <summary>
/// Both ends of the socket without the socket: the message built and parsed as the tray reads it,
/// the response built and parsed as the caller reads it. Without the bytes, a benchmark measured
/// the handler and none of what the wire costs.
/// </summary>
sealed class WireClient(MessageHandler handler) : IProtocolClient
{
    public async Task<Response> Send(Message message, Cancel cancel)
    {
        Message.TryParse(message.Build(), out var received);
        var response = await handler.Handle(received!);
        Response.TryParse(response.Build(), out var parsed);
        return parsed!;
    }
}
