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
        // On Linux, disposing the listener while an accept is pending can abort the accept with
        // a SocketException before the cancellation reaches it, so shutdown would throw.
        catch (SocketException) when (cancel.IsCancellationRequested)
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
                if (!Message.TryParse(text.Span, out var message))
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

                await stream.WriteAsync(response.Build(), cancel);
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
    /// answer the tray has to fetch from a CI service does. Kept as the UTF-8 that arrived rather
    /// than decoded a chunk at a time into a builder, which held a log as UTF-16 twice over before
    /// parsing began.
    /// </summary>
    public static async Task<ReadOnlyMemory<byte>> ReadMessage(Stream stream, Cancel cancel, TimeSpan? readTimeout = null)
    {
        using var timeout = CancelSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(readTimeout ?? ReadTimeout);
        var buffer = new byte[4096];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (EndsMessage(buffer.AsSpan(0, length)))
            {
                break;
            }
        }

        return buffer.AsMemory(0, length);
    }

    /// <summary>
    /// Whether what has been read ends in the empty line. Read off the last bytes rather than off
    /// the whole text, which a log of a few megabytes would scan once a chunk.
    /// </summary>
    static bool EndsMessage(ReadOnlySpan<byte> text) =>
        text.EndsWith("\n\n"u8) ||
        text.EndsWith("\r\n\r\n"u8);

    public void Dispose() =>
        listener.Stop();
}
