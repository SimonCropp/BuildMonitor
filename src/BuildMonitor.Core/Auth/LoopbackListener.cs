/// <summary>
/// The redirect target of the browser flow: a socket on 127.0.0.1 that answers exactly one
/// request. A raw listener rather than HttpListener, which on Windows goes through http.sys and
/// its URL reservations.
/// </summary>
sealed class LoopbackListener : IDisposable
{
    readonly TcpListener listener;

    public LoopbackListener(int port = 0)
    {
        listener = new(IPAddress.Loopback, port);
        listener.Start();
        Port = ((IPEndPoint) listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    /// <summary>
    /// The query of the first request that carries a code or an error. A favicon probe or a
    /// stray request gets a 404 and does not count.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallback(Cancel cancel)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancel);
            await using var stream = client.GetStream();
            var requestLine = await ReadRequestLine(stream, cancel);
            var query = Parse(requestLine);
            if (query.ContainsKey("code") ||
                query.ContainsKey("error"))
            {
                await Respond(stream, "200 OK", "<html><body style=\"font-family:sans-serif\"><h2>BuildMonitor</h2><p>Signed in. You can close this window.</p></body></html>", cancel);
                return query;
            }

            await Respond(stream, "404 Not Found", "", cancel);
        }
    }

    static async Task<string> ReadRequestLine(NetworkStream stream, Cancel cancel)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancel);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                return text[..end];
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    /// <summary>
    /// "GET /callback?code=x&amp;state=y HTTP/1.1" to its query parameters.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var target = parts[1];
        var question = target.IndexOf('?');
        if (question < 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var result = new Dictionary<string, string>();
        foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? "" : pair[(equals + 1)..];
            result[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    static async Task Respond(NetworkStream stream, string status, string body, Cancel cancel)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancel);
        await stream.WriteAsync(bytes, cancel);
        await stream.FlushAsync(cancel);
    }

    public void Dispose() =>
        listener.Stop();
}
