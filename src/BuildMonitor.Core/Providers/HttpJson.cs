/// <summary>
/// The one HTTP client shape every provider uses: a base address, a credential applied per
/// <see cref="AuthScheme"/>, JSON in and out through source generated contexts, and the two
/// failures the poller cares about surfaced as their own exceptions.
/// <para>
/// GET responses carrying an ETag are cached and revalidated with If-None-Match. GitHub answers
/// an unchanged resource with a 304 that does not count against the rate limit, which is what
/// makes polling a hundred repositories every thirty seconds affordable. The cache is the
/// caller's, because this client lives for one poll; see <see cref="ETagCache"/>. So is the
/// <see cref="RateBudget"/> every response is recorded in.
/// </para>
/// </summary>
sealed class HttpJson : IDisposable
{
    HttpClient client;
    ETagCache cache;
    RateBudget? budget;

    public HttpJson(
        HttpMessageHandler handler,
        Uri baseAddress,
        AuthScheme scheme,
        string? secret,
        string? user,
        IEnumerable<KeyValuePair<string, string>>? headers = null,
        ETagCache? cache = null,
        RateBudget? budget = null)
    {
        this.cache = cache ?? new();
        this.budget = budget;
        client = new(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress,
            // Azure DevOps delays a throttled request by up to thirty seconds before answering it,
            // and a thirty second timeout turned that delay into an error.
            Timeout = TimeSpan.FromSeconds(75)
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

    public bool IsCached(string path) =>
        cache.Contains(Resolve(path).ToString());

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
        var requested = Resolve(path);
        var key = requested.ToString();
        var cached = cache.TryGet(key, out var entry);
        if (cached)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", entry.ETag);
        }

        using var response = await Exchange(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (response.StatusCode == HttpStatusCode.NotModified &&
            cached)
        {
            return entry.Body;
        }

        await Throw(response, requested, cancel);
        var body = await response.Content.ReadAsByteArrayAsync(cancel);
        var etag = response.Headers.ETag?.ToString();
        if (etag is not null)
        {
            cache.Set(key, etag, body);
        }

        return body;
    }

    public async Task<T> Send<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> info, Cancel cancel)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        using var response = await Exchange(request, HttpCompletionOption.ResponseContentRead, cancel);
        await Throw(response, Resolve(path), cancel);
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

        using var response = await Exchange(request, HttpCompletionOption.ResponseContentRead, cancel);
        await Throw(response, Resolve(path), cancel);
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

        using var response = await Exchange(request, HttpCompletionOption.ResponseContentRead, cancel);
        var requested = Resolve(path);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ||
            SignInPage(response, requested))
        {
            await Throw(response, requested, cancel);
        }

        return response.StatusCode;
    }

    public static StringContent Json<T>(T body, JsonTypeInfo<T> info) =>
        new(JsonSerializer.Serialize(body, info), Encoding.UTF8, "application/json");

    public static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    async Task<HttpResponseMessage> Exchange(HttpRequestMessage request, HttpCompletionOption completion, Cancel cancel)
    {
        budget?.Sent();
        var response = await client.SendAsync(request, completion, cancel);
        budget?.Record(response);
        return response;
    }

    Uri Resolve(string path) =>
        new(client.BaseAddress!, path);

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

    static async Task Throw(HttpResponseMessage response, Uri requested, Cancel cancel)
    {
        if (SignInPage(response, requested))
        {
            throw new AuthException($"{(int) response.StatusCode} {response.ReasonPhrase}: answered with a sign in page");
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await Text(response, cancel);
        var now = DateTimeOffset.UtcNow;
        var observation = RateHeaders.Read(response.Headers, now);
        if (response.StatusCode == HttpStatusCode.TooManyRequests ||
            (response.StatusCode == HttpStatusCode.Forbidden && RateLimited(observation, text)))
        {
            throw new RateLimitException(RetryAfter(observation, now));
        }

        var body = Snippet(text);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AuthException($"{(int) response.StatusCode} {response.ReasonPhrase}{body}", response.StatusCode);
        }

        throw new HttpRequestException($"{(int) response.StatusCode} {response.ReasonPhrase} from {response.RequestMessage?.RequestUri}{body}", null, response.StatusCode);
    }

    /// <summary>
    /// Azure DevOps refuses a wrong or expired personal access token with a redirect to its sign
    /// in page, or a 203 carrying that page, rather than a 401. The handler follows the redirect,
    /// so the refusal arrived as a successful HTML response, failed JSON parsing, and backed off as
    /// an error instead of asking the user to sign in again.
    /// </summary>
    static bool SignInPage(HttpResponseMessage response, Uri requested)
    {
        if (response.Content.Headers.ContentType?.MediaType != "text/html")
        {
            return false;
        }

        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
        {
            return true;
        }

        return response.IsSuccessStatusCode &&
               response.RequestMessage?.RequestUri is { } landed &&
               !string.Equals(landed.Host, requested.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GitHub answers its secondary rate limits with a 403 that leaves the hourly quota untouched,
    /// so remaining is not 0. Read as a refused credential, one burst of requests put the
    /// connection into sign in required and stopped polling until the user acted.
    /// </summary>
    static bool RateLimited(RateObservation observation, string text) =>
        observation.Remaining == 0 ||
        observation.RetryAfter is not null ||
        text.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("abuse detection", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The reset is when the whole quota comes back, which only matters once it has run out. A
    /// 429 with quota left is a secondary limit, and waiting for the reset stalled the connection
    /// for up to an hour; with no time named, the poller backs off from a minute instead.
    /// </summary>
    static TimeSpan? RetryAfter(RateObservation observation, DateTimeOffset now)
    {
        if (observation.RetryAfter is { } retryAfter)
        {
            return retryAfter;
        }

        if (observation.Remaining == 0 &&
            observation.Reset is { } reset &&
            reset > now)
        {
            return reset - now;
        }

        return null;
    }

    static async Task<string> Text(HttpResponseMessage response, Cancel cancel)
    {
        try
        {
            return (await response.Content.ReadAsStringAsync(cancel)).Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    static string Snippet(string text)
    {
        if (text.Length == 0)
        {
            return "";
        }

        return $": {(text.Length > 200 ? text[..200] : text)}";
    }

    public void Dispose() =>
        client.Dispose();
}
