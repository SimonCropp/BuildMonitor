/// <summary>
/// The one HTTP client shape every provider uses: a base address, a credential applied per
/// <see cref="AuthScheme"/>, JSON in and out through source generated contexts, and the two
/// failures the poller cares about surfaced as their own exceptions.
/// <para>
/// GET responses carrying an ETag are cached, as the value parsed from them, and revalidated with
/// If-None-Match. GitHub answers an unchanged resource with a 304 that does not count against the
/// rate limit, which is what makes polling a hundred repositories every thirty seconds affordable.
/// The cache is the caller's, because this client lives for one poll; see <see cref="ETagCache"/>.
/// So is the <see cref="RateBudget"/> every response is recorded in.
/// </para>
/// </summary>
sealed class HttpJson : IDisposable
{
    /// <summary>
    /// How long a call is given. Azure DevOps delays a throttled request by up to thirty seconds
    /// before answering it, and a thirty second limit turned that delay into an error.
    /// <para>
    /// Armed per call rather than through <see cref="HttpClient.Timeout"/>, which is a deadline for
    /// the whole exchange: under <see cref="HttpCompletionOption.ResponseHeadersRead"/> it stays
    /// armed while the body is read, so a file that takes longer than this to arrive fails however
    /// fast it is arriving. A download re-arms its deadline as each chunk lands, which is what lets
    /// this mean "nothing arrived for 75 seconds" there rather than "it took 75 seconds".
    /// </para>
    /// </summary>
    static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(75);

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
            // The deadline is each call's, through Deadline, so that a download can hold one open
            // for as long as bytes keep arriving. See requestTimeout.
            Timeout = Timeout.InfiniteTimeSpan
        };
        var defaultHeaders = client.DefaultRequestHeaders;
        defaultHeaders.UserAgent.ParseAdd("BuildMonitor");
        defaultHeaders.Accept.ParseAdd("application/json");
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                defaultHeaders.Remove(name);
                defaultHeaders.TryAddWithoutValidation(name, value);
            }
        }

        if (secret is not null)
        {
            Credential.Apply(defaultHeaders, scheme, secret, user);
        }
    }

    public Uri BaseAddress => client.BaseAddress!;

    public bool IsCached(string path) =>
        cache.Contains(Resolve(path).ToString());

    /// <summary>
    /// <paramref name="accept"/> replaces the JSON the client asks for otherwise, for a route that
    /// answers JSON but refuses the Accept an API route wants: GoCD serves its artifact listing
    /// from the file routes, which 404 anything but a type they know.
    /// </summary>
    public Task<T> Get<T>(string path, JsonTypeInfo<T> info, Cancel cancel, string? accept = null) =>
        GetParsed(path, json: true, (content, token) => Deserialize(content, info, path, token), cancel, accept);

    public Task<string> GetText(string path, Cancel cancel) =>
        GetParsed(path, json: false, (content, token) => content.ReadAsStringAsync(token), cancel);

    /// <summary>
    /// One header of the answer to a GET, or null when the answer has none. Sent without
    /// If-None-Match even where an ETag is cached: which headers a 304 repeats is up to the service,
    /// and one left off would read as a header the service never sends.
    /// </summary>
    public async Task<string?> GetHeader(string path, string name, Cancel cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        await Throw(response, Resolve(path), deadline.Token);
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return string.Join(',', values);
    }

    /// <summary>
    /// A build log, as text. Never cached: a log can run to megabytes, and the cache would hold it
    /// for as long as the client lives. <paramref name="accept"/> replaces the JSON the client asks
    /// for otherwise, which one service answers with the log's lines as a JSON array and another
    /// refuses with a 406. A web page is refused as it is where JSON was expected, because a sign
    /// in page or a web app on the clipboard is no log.
    /// </summary>
    public async Task<string> GetLog(string path, Cancel cancel, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        await Throw(response, Resolve(path), deadline.Token);
        await ThrowIfHtml(response, "a log", deadline.Token);
        return await response.Content.ReadAsStringAsync(deadline.Token);
    }

    /// <summary>
    /// A file, copied to <paramref name="destination"/> as it arrives rather than read into memory:
    /// an artifact runs to hundreds of megabytes, and a tray holding one would be paged out before
    /// it finished. Never cached, as a log is not. <paramref name="accept"/> replaces the JSON the
    /// client asks for otherwise, which a file endpoint answers with a 406, or with a description of
    /// the file in place of the file. A web page is refused where a file was expected, because a
    /// sign in page saved as a zip is worse than an error.
    /// <para>
    /// Stops at <paramref name="maxBytes"/> rather than filling a disk with a build output nobody
    /// asked for. A declared length over the limit is refused before a byte is read; a service that
    /// declares none, as Jenkins does, is caught while copying instead. Returns the bytes written.
    /// </para>
    /// </summary>
    public async Task<long> Download(string path, Stream destination, long maxBytes, Cancel cancel, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        // Always replaced, unlike a log's, which may keep the default: the client asks for JSON, and
        // a file endpoint that honours that answers with a description of the file, not the file.
        request.Headers.Accept.ParseAdd(accept ?? "*/*");
        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        await Throw(response, Resolve(path), deadline.Token);
        await ThrowIfHtml(response, "a file", deadline.Token);
        if (response.Content.Headers.ContentLength is { } declared &&
            declared > maxBytes)
        {
            throw new ArtifactTooLargeException(declared, maxBytes);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        // What Stream.CopyTo uses, and under the 85,000 bytes that would put it on the large object
        // heap for the life of the download.
        var buffer = new byte[81920];
        var total = 0L;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, deadline.Token);
            if (read == 0)
            {
                return total;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new ArtifactTooLargeException(null, maxBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
            // Pushed out as each chunk lands, so the deadline means "nothing arrived for 75 seconds"
            // rather than "the file took 75 seconds", which no large artifact would survive.
            deadline.CancelAfter(requestTimeout);
        }
    }

    /// <summary>
    /// A GET revalidated against the value cached for its URL, which a 304 hands back without
    /// parsing again. A value of another type counts as nothing cached, and a body is kept only once
    /// it has parsed, so a 304 never answers for one that did not.
    /// </summary>
    async Task<T> GetParsed<T>(string path, bool json, Func<HttpContent, Cancel, Task<T>> parse, Cancel cancel, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        var requested = Resolve(path);
        var key = requested.ToString();
        var cached = cache.TryGet(key, out var entry) &&
                     entry.Value is T;
        if (cached)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", entry.ETag);
        }

        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotModified &&
            cached)
        {
            return (T) entry.Value;
        }

        await Throw(response, requested, deadline.Token);
        if (json)
        {
            await ThrowIfHtml(response, "JSON", deadline.Token);
        }

        var value = await parse(response.Content, deadline.Token);
        var etag = response.Headers.ETag?.ToString();
        if (etag is not null)
        {
            cache.Set(key, etag, value!);
        }

        return value;
    }

    public async Task<T> Send<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> info, Cancel cancel)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
        await Throw(response, Resolve(path), deadline.Token);
        await ThrowIfHtml(response, "JSON", deadline.Token);
        return await Deserialize(response.Content, info, path, deadline.Token);
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

        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
        await Throw(response, Resolve(path), deadline.Token);
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

        using var deadline = Deadline(cancel);
        using var response = await Exchange(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
        var requested = Resolve(path);
        if (response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden or
                HttpStatusCode.TooManyRequests ||
            SignInPage(response, requested))
        {
            await Throw(response, requested, deadline.Token);
        }

        return response.StatusCode;
    }

    public static StringContent Json<T>(T body, JsonTypeInfo<T> info) =>
        new(JsonSerializer.Serialize(body, info), Encoding.UTF8, "application/json");

    public static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    /// <summary>
    /// The token a call runs under: the caller's, with <see cref="requestTimeout"/> on top. Held by
    /// the caller for as long as the response is, so the deadline still covers reading the body the
    /// way <see cref="HttpClient.Timeout"/> did, and so a download can push it out per chunk.
    /// </summary>
    static CancelSource Deadline(Cancel cancel)
    {
        var deadline = CancelSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(requestTimeout);
        return deadline;
    }

    async Task<HttpResponseMessage> Exchange(HttpRequestMessage request, HttpCompletionOption completion, Cancel cancel)
    {
        budget?.Sent();
        var response = await client.SendAsync(request, completion, cancel);
        budget?.Record(response);
        return response;
    }

    Uri Resolve(string path) =>
        new(client.BaseAddress!, path);

    /// <summary>
    /// Parsed as the body arrives rather than read into an array first, which for a response of a
    /// few hundred kilobytes, as GitHub's runs are, put an array on the large object heap each time.
    /// </summary>
    static async Task<T> Deserialize<T>(HttpContent content, JsonTypeInfo<T> info, string path, Cancel cancel)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancel);
            return await JsonSerializer.DeserializeAsync(stream, info, cancel) ??
                   throw new HttpRequestException($"Empty response from {path}");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException($"Unexpected response from {path}: {exception.Message}", exception);
        }
    }

    static async Task Throw(HttpResponseMessage response, Uri requested, Cancel cancel)
    {
        var status = response.StatusCode;
        if (SignInPage(response, requested))
        {
            throw new AuthException($"{(int) status} {response.ReasonPhrase}: answered with a sign in page");
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await Text(response, cancel);
        var now = DateTimeOffset.UtcNow;
        var observation = RateHeaders.Read(response.Headers, now);
        if (status == HttpStatusCode.TooManyRequests ||
            (status == HttpStatusCode.Forbidden && RateLimited(observation, text)))
        {
            throw new RateLimitException(RetryAfter(observation, now));
        }

        var body = Snippet(text);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AuthException($"{(int) status} {response.ReasonPhrase}{body}", status);
        }

        throw new HttpRequestException($"{(int) status} {response.ReasonPhrase} from {response.RequestMessage?.RequestUri}{body}", null, status);
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

        return response is
               {
                   IsSuccessStatusCode: true,
                   RequestMessage.RequestUri: { } landed
               } &&
               !string.Equals(landed.Host, requested.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A server with no route for a path can answer it with a 200 carrying its web app, as
    /// AppVeyor does for some api/account paths, and a server address that points at a web UI or
    /// a proxy gets such a page for every path. Parsed as JSON, the page failed on its opening
    /// angle bracket with an error naming neither the status nor what arrived, and a page sent
    /// with an ETag was cached as a body to revalidate. The exception carries no status, as that
    /// parse failure did, because GitLab reads a GraphQL request failing without one as a server
    /// with no GraphQL and fetches over REST.
    /// </summary>
    static async Task ThrowIfHtml(HttpResponseMessage response, string expected, Cancel cancel)
    {
        if (response.Content.Headers.ContentType?.MediaType != "text/html")
        {
            return;
        }

        var text = await Text(response, cancel);
        throw new HttpRequestException($"{(int) response.StatusCode} {response.ReasonPhrase} from {response.RequestMessage?.RequestUri}: text/html where {expected} was expected{Snippet(text)}");
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

        if (observation is
            {
                Remaining: 0,
                Reset: { } reset
            } &&
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

    /// <summary>
    /// A body that is not JSON, most often an HTML page, runs over many indented lines, and the
    /// message ends up in a connection's status on a single row.
    /// </summary>
    static string Snippet(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length == 0)
        {
            return "";
        }

        return $": {(flat.Length > 200 ? flat[..200] : flat)}";
    }

    public void Dispose() =>
        client.Dispose();
}
