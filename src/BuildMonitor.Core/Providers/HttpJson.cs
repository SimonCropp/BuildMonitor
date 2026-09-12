/// <summary>
/// The one HTTP client shape every provider uses: a base address, a credential applied per
/// <see cref="AuthScheme"/>, JSON in and out through source generated contexts, and the two
/// failures the poller cares about surfaced as their own exceptions.
/// <para>
/// GET responses carrying an ETag are cached and revalidated with If-None-Match. GitHub answers
/// an unchanged resource with a 304 that does not count against the rate limit, which is what
/// makes polling a hundred repositories every thirty seconds affordable.
/// </para>
/// </summary>
sealed class HttpJson : IDisposable
{
    readonly HttpClient client;
    readonly ConcurrentDictionary<string, (string ETag, byte[] Body)> cache = new();

    public HttpJson(
        HttpMessageHandler handler,
        Uri baseAddress,
        AuthScheme scheme,
        string? secret,
        string? user,
        IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        client = new(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BuildMonitor");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                client.DefaultRequestHeaders.Remove(name);
                client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            }
        }

        if (secret is not null)
        {
            Credential.Apply(client.DefaultRequestHeaders, scheme, secret, user);
        }
    }

    public Uri BaseAddress => client.BaseAddress!;

    public async Task<T> Get<T>(string path, JsonTypeInfo<T> info, Cancel cancel)
    {
        var bytes = await GetBytes(path, cancel);
        return Deserialize(bytes, info, path);
    }

    public async Task<string> GetText(string path, Cancel cancel) =>
        Encoding.UTF8.GetString(await GetBytes(path, cancel));

    async Task<byte[]> GetBytes(string path, Cancel cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var key = new Uri(client.BaseAddress!, path).ToString();
        var cached = cache.TryGetValue(key, out var entry);
        if (cached)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", entry.ETag);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (response.StatusCode == HttpStatusCode.NotModified &&
            cached)
        {
            return entry.Body;
        }

        await Throw(response, cancel);
        var body = await response.Content.ReadAsByteArrayAsync(cancel);
        var etag = response.Headers.ETag?.ToString();
        if (etag is not null)
        {
            cache[key] = (etag, body);
        }

        return body;
    }

    public async Task<T> Send<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> info, Cancel cancel)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        using var response = await client.SendAsync(request, cancel);
        await Throw(response, cancel);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        return Deserialize(bytes, info, path);
    }

    public async Task Send(HttpMethod method, string path, HttpContent? content, Cancel cancel, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await client.SendAsync(request, cancel);
        await Throw(response, cancel);
    }

    /// <summary>
    /// Sends and reports the status rather than throwing, for the calls whose failure is an
    /// answer: a crumb issuer that does not exist, a build endpoint that wants parameters.
    /// </summary>
    public async Task<HttpStatusCode> TrySend(HttpMethod method, string path, HttpContent? content, Cancel cancel, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await client.SendAsync(request, cancel);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            await Throw(response, cancel);
        }

        return response.StatusCode;
    }

    public static StringContent Json<T>(T body, JsonTypeInfo<T> info) =>
        new(JsonSerializer.Serialize(body, info), Encoding.UTF8, "application/json");

    public static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    static T Deserialize<T>(byte[] bytes, JsonTypeInfo<T> info, string path)
    {
        try
        {
            return JsonSerializer.Deserialize(bytes, info) ??
                   throw new HttpRequestException($"Empty response from {path}");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException($"Unexpected response from {path}: {exception.Message}", exception);
        }
    }

    static async Task Throw(HttpResponseMessage response, Cancel cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var retryAfter = RetryAfter(response);
        if (response.StatusCode == HttpStatusCode.TooManyRequests ||
            (response.StatusCode == HttpStatusCode.Forbidden && RateLimitExhausted(response)))
        {
            throw new RateLimitException(retryAfter ?? TimeSpan.FromMinutes(5));
        }

        var body = await Snippet(response, cancel);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AuthException($"{(int) response.StatusCode} {response.ReasonPhrase}{body}");
        }

        throw new HttpRequestException($"{(int) response.StatusCode} {response.ReasonPhrase} from {response.RequestMessage?.RequestUri}{body}", null, response.StatusCode);
    }

    static bool RateLimitExhausted(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) &&
        values.FirstOrDefault() == "0";

    static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta)
            {
                return delta;
            }

            if (header.Date is { } date)
            {
                return date - DateTimeOffset.UtcNow;
            }
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch) - DateTimeOffset.UtcNow;
        }

        return null;
    }

    static async Task<string> Snippet(HttpResponseMessage response, Cancel cancel)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancel);
            text = text.Trim();
            if (text.Length == 0)
            {
                return "";
            }

            return $": {(text.Length > 200 ? text[..200] : text)}";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public void Dispose() =>
        client.Dispose();
}
