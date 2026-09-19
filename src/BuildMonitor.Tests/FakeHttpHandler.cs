/// <summary>
/// Canned responses keyed by method and URL, and a record of every request, so a provider test
/// asserts both what it parsed and what it sent.
/// </summary>
class FakeHttpHandler : HttpMessageHandler
{
    Dictionary<string, FakeResponse> responses = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];
    public List<HttpRequestHeaders> RequestHeaders { get; } = [];

    public FakeHttpHandler Map(string method, string url, string body, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers)
    {
        responses[$"{method} {url}"] = new(status, body, headers);
        return this;
    }

    public FakeHttpHandler Get(
        string url,
        string body) =>
        Map("GET", url, body);

    /// <summary>
    /// An HTML page where JSON was expected, optionally arriving from another address, as a
    /// response does once a real handler has followed a redirect to a sign in page.
    /// </summary>
    public FakeHttpHandler MapHtml(string method, string url, string body, HttpStatusCode status = HttpStatusCode.OK, string? landedOn = null, params (string Name, string Value)[] headers)
    {
        responses[$"{method} {url}"] = new(status, body, headers, "text/html", landedOn);
        return this;
    }

    /// <summary>
    /// A file rather than text. <paramref name="declareLength"/> false sends no Content-Length, as
    /// Jenkins does for an artifact: a download cannot weigh that one against its limit before it
    /// starts, and has to hold to it while copying instead.
    /// </summary>
    public FakeHttpHandler MapBytes(string method, string url, byte[] body, string mediaType = "application/zip", bool declareLength = true)
    {
        responses[$"{method} {url}"] = new(HttpStatusCode.OK, "", [], mediaType, null, body, declareLength);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
    {
        var url = request.RequestUri!.ToString();
        var line = $"{request.Method} {url}";
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancel);
            if (body.Length > 0)
            {
                line += $"\n  {body}";
            }
        }

        var extra = request.Headers
            .Where(_ => _.Key is "X-GoCD-Confirm" or "Jenkins-Crumb" or "If-None-Match" or "Referer")
            .Select(_ => $"\n  {_.Key}: {string.Join(',', _.Value)}");
        // Providers fan requests out; the record stays in arrival order under a lock.
        lock (Requests)
        {
            Requests.Add(line + string.Concat(extra));
            RequestHeaders.Add(request.Headers);
        }

        var key = $"{request.Method} {url}";
        if (!responses.TryGetValue(key, out var response))
        {
            var withoutQuery = url.IndexOf('?') is var index and >= 0 ? url[..index] : url;
            if (!responses.TryGetValue($"{request.Method} {withoutQuery}", out response))
            {
                return new(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    Content = new StringContent($"No canned response for {key}")
                };
            }
        }

        var message = new HttpResponseMessage(response.Status)
        {
            RequestMessage = response.LandedOn is null ? request : new(request.Method, response.LandedOn),
            Content = Body(response)
        };
        foreach (var (name, value) in response.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        return message;
    }

    static HttpContent Body(FakeResponse response)
    {
        if (response.Bytes is not { } bytes)
        {
            return new StringContent(response.Body, Encoding.UTF8, response.MediaType);
        }

        HttpContent content = response.DeclareLength
            ? new ByteArrayContent(bytes)
            : new StreamContent(new UnmeasuredStream(bytes));
        content.Headers.ContentType = new(response.MediaType);
        return content;
    }

    record FakeResponse(
        HttpStatusCode Status,
        string Body,
        (string Name, string Value)[] Headers,
        string MediaType = "application/json",
        string? LandedOn = null,
        byte[]? Bytes = null,
        bool DeclareLength = true);

    /// <summary>
    /// A stream that refuses to say how long it is, so <see cref="StreamContent"/> computes no
    /// Content-Length for it. A seekable stream would declare one, which is the case under test.
    /// </summary>
    sealed class UnmeasuredStream(byte[] bytes) :
        Stream
    {
        MemoryStream inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
