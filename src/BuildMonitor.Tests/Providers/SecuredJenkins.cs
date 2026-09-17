/// <summary>
/// Just enough of a Jenkins that anonymous users may not read, served over a real socket so a
/// real handler follows its redirects. FakeHttpHandler answers each request as it was sent. So it
/// never showed that the handler follows the redirect a stop answers with, and drops the
/// credential on the way.
/// </summary>
sealed class SecuredJenkins : IAsyncDisposable
{
    TcpListener listener = new(IPAddress.Loopback, 0);
    CancelSource stop = new();
    Task serving;

    public SecuredJenkins()
    {
        listener.Start();
        Server = $"http://127.0.0.1:{((IPEndPoint) listener.LocalEndpoint).Port}";
        serving = Serve(stop.Token);
    }

    public string Server { get; }

    /// <summary>
    /// Each request's method and path, in arrival order, marked when it carried a credential.
    /// </summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    async Task Serve(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancel);
                await using var stream = client.GetStream();
                await Answer(stream, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that hung up takes nothing else down.
            }
        }
    }

    async Task Answer(NetworkStream stream, Cancel cancel)
    {
        var (method, path, headers) = await Read(stream, cancel);
        var signedIn = headers.ContainsKey("Authorization");
        Requests.Enqueue(signedIn ? $"{method} {path} (signed in)" : $"{method} {path}");
        if (path == "/whoAmI/api/json")
        {
            // An UnprotectedRootAction: anyone may read it.
            await Respond(stream, "200 OK", "application/json", """{"name":"anonymous"}""", null, cancel);
            return;
        }

        if (!signedIn)
        {
            await Respond(stream, "403 Forbidden", "text/html", "<html><body>Authentication required</body></html>", null, cancel);
            return;
        }

        if (method == "POST" &&
            path.EndsWith("/stop", StringComparison.Ordinal))
        {
            // Stapler's forwardToPreviousPage: back to the Referer, or to the build without one.
            var location = headers.GetValueOrDefault("Referer") ?? path[..^"stop".Length];
            await Respond(stream, "302 Found", "text/plain", "", location, cancel);
            return;
        }

        await Respond(stream, "404 Not Found", "text/plain", "", null, cancel);
    }

    static async Task<(string Method, string Path, Dictionary<string, string> Headers)> Read(NetworkStream stream, Cancel cancel)
    {
        var received = new List<byte>();
        var buffer = new byte[4096];
        var end = -1;
        while (end < 0)
        {
            var read = await stream.ReadAsync(buffer, cancel);
            if (read == 0)
            {
                throw new IOException("The client closed the connection mid request");
            }

            received.AddRange(buffer[..read]);
            end = Encoding.ASCII.GetString(received.ToArray()).IndexOf("\r\n\r\n", StringComparison.Ordinal);
        }

        var lines = Encoding.ASCII.GetString(received.ToArray(), 0, end).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            headers[line[..colon]] = line[(colon + 1)..].Trim();
        }

        // Unread body bytes would make closing the socket reset the connection before the answer is read.
        var body = received.Count - end - 4;
        if (headers.TryGetValue("Content-Length", out var length))
        {
            var remaining = int.Parse(length) - body;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancel);
                if (read == 0)
                {
                    break;
                }

                remaining -= read;
            }
        }

        return (requestLine[0], requestLine[1], headers);
    }

    static async Task Respond(NetworkStream stream, string status, string type, string body, string? location, Cancel cancel)
    {
        var content = Encoding.UTF8.GetBytes(body);
        var header = new StringBuilder($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {content.Length}\r\nConnection: close\r\n");
        if (location is not null)
        {
            header.Append($"Location: {location}\r\n");
        }

        header.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), cancel);
        await stream.WriteAsync(content, cancel);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Stop();
        await serving;
        stop.Dispose();
    }
}
