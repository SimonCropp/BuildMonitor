/// <summary>
/// Canned responses keyed by method and URL, and a record of every request, so a provider test
/// asserts both what it parsed and what it sent.
/// </summary>
class FakeHttpHandler : HttpMessageHandler
{
    readonly Dictionary<string, FakeResponse> responses = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];
    public List<HttpRequestHeaders> RequestHeaders { get; } = [];

    public FakeHttpHandler Map(string method, string url, string body, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers)
    {
        responses[$"{method} {url}"] = new(status, body, headers);
        return this;
    }

    public FakeHttpHandler Get(string url, string body) =>
        Map("GET", url, body);

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
            .Where(_ => _.Key is "X-GoCD-Confirm" or "Jenkins-Crumb" or "If-None-Match")
            .Select(_ => $"\n  {_.Key}: {string.Join(",", _.Value)}");
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
            RequestMessage = request,
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
        };
        foreach (var (name, value) in response.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        return message;
    }

    record FakeResponse(HttpStatusCode Status, string Body, (string Name, string Value)[] Headers);
}
