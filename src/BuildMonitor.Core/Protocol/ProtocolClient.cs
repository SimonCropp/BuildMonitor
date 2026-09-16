/// <summary>
/// The launcher's and the MCP server's side of the socket. One connection per message.
/// </summary>
sealed class ProtocolClient(int port) : IProtocolClient
{
    int Port { get; } = port;

    /// <summary>
    /// How long the verb may take. A read is answered from the state at once, but a retry or a
    /// cancel is a call to the CI service, and a log is a call for the jobs and then one a job.
    /// The tray sends nothing until it has the answer, so this is also how long the read waits.
    /// </summary>
    static TimeSpan Limit(Verb verb) =>
        verb switch
        {
            Verb.Retry or Verb.Cancel => TimeSpan.FromSeconds(30),
            Verb.Log => TimeSpan.FromSeconds(60),
            _ => TimeSpan.FromSeconds(3)
        };

    public async Task<Response> Send(Message message, Cancel cancel)
    {
        var limit = Limit(message.Verb);
        using var timeout = CancelSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(limit);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(message.Build()), timeout.Token);
            await stream.FlushAsync(timeout.Token);
            var text = await LocalServer.ReadMessage(stream, timeout.Token, limit);
            if (Response.TryParse(text, out var response))
            {
                return response;
            }

            return Response.Error("Unreadable response");
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            throw new TrayUnreachableException(Port, exception);
        }
    }

    /// <summary>
    /// Whether a tray answers on the port. A refused connection answers at once; a firewall in
    /// stealth mode makes it wait out the timeout instead.
    /// </summary>
    public async Task<bool> IsRunning(Cancel cancel)
    {
        try
        {
            var response = await Send(new(Verb.Ping), cancel);
            return response.Ok;
        }
        catch (TrayUnreachableException)
        {
            return false;
        }
    }
}

interface IProtocolClient
{
    Task<Response> Send(Message message, Cancel cancel);
}

sealed class TrayUnreachableException(int port, Exception inner) : Exception($"No BuildMonitor tray is listening on port {port}", inner);
