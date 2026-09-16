/// <summary>
/// The tray's loopback listener. Binding the port is also the single instance gate: whoever
/// holds it is the tray, and a second start that cannot bind sends the first a show instead.
/// </summary>
sealed class LocalServer : IDisposable
{
    TcpListener listener;

    LocalServer(TcpListener listener, int port)
    {
        this.listener = listener;
        Port = port;
    }

    public int Port { get; }

    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    public static bool TryBind(int port, [NotNullWhen(true)] out LocalServer? server)
    {
        server = null;
        var listener = new TcpListener(IPAddress.Loopback, port)
        {
            // Without this a second bind succeeds on some platforms and two trays race.
            ExclusiveAddressUse = true
        };
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            return false;
        }

        server = new(listener, ((IPEndPoint) listener.LocalEndpoint).Port);
        return true;
    }

    /// <summary>
    /// Accepts until cancelled. Each client is answered on its own task, because a retry can
    /// take seconds and the launcher's ping should not wait behind it.
    /// </summary>
    public async Task Listen(Func<Message, Task<Response>> handle, Cancel cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancel);
                _ = Task.Run(() => Serve(client, handle, cancel), Cancel.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static async Task Serve(TcpClient client, Func<Message, Task<Response>> handle, Cancel cancel)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var text = await ReadMessage(stream, cancel);
                Response response;
                if (!Message.TryParse(text, out var message))
                {
                    response = Response.Error("Unreadable message");
                }
                else
                {
                    try
                    {
                        response = await handle(message);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Handling {Verb} failed", message.Verb);
                        response = Response.Error(exception.Message);
                    }
                }

                await stream.WriteAsync(Encoding.UTF8.GetBytes(response.Build()), cancel);
                await stream.FlushAsync(cancel);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                // The client went away; nothing to answer.
            }
        }
    }

    /// <summary>
    /// Reads until the empty line that ends a message, or the client stops sending. The wait is
    /// <see cref="ReadTimeout"/> unless the caller allows longer, which the side waiting on an
    /// answer the tray has to fetch from a CI service does.
    /// </summary>
    public static async Task<string> ReadMessage(Stream stream, Cancel cancel, TimeSpan? readTimeout = null)
    {
        using var timeout = CancelSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(readTimeout ?? ReadTimeout);
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (EndsMessage(builder))
            {
                break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether what has been read ends in the empty line. Read off the last characters rather
    /// than off the whole text, which a log of a few megabytes would copy once a chunk.
    /// </summary>
    static bool EndsMessage(StringBuilder builder) =>
        builder is [.., _, '\n'] &&
        (builder[^2] == '\n' ||
         builder is [.., '\r', '\n', '\r', _]);

    public void Dispose() =>
        listener.Stop();
}
