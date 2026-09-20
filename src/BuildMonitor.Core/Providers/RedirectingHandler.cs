/// <summary>
/// Follows redirects here rather than leaving them to the transport, so that a redirect which stays
/// inside one service keeps the credential the request was sent with.
/// <para>
/// <see cref="SocketsHttpHandler"/> strips the Authorization header from any redirect that leaves
/// the host it was sent to, and cannot be told otherwise. Azure DevOps serves an artifact by
/// redirecting from the organization's address to a separate artifacts host that still wants the
/// personal access token, so the download landed there anonymous and was answered with a sign in
/// page as a 203, which read as a refused token: the listing beside it, which never leaves the
/// organization's host, kept working and made it look like the token was scoped wrong.
/// </para>
/// <para>
/// The default is unchanged, because <see cref="CredentialHosts"/> carries the credential across
/// only where both hosts are the one service. Everywhere else it is stripped, as the transport
/// stripped it, which is what makes GitHub and Bitbucket work: they redirect to storage that
/// carries its own signature in the URL and refuses a credential it does not know.
/// </para>
/// </summary>
sealed class RedirectingHandler(HttpMessageHandler inner) :
    DelegatingHandler(inner)
{
    /// <summary>
    /// A service that answers a redirect with another redirect forever is a loop, and the poller
    /// would sit in it until the call's deadline. Well past any chain a CI service sends, which runs
    /// to two: the API's address to the artifacts host, and on to the blob that holds the file.
    /// </summary>
    const int maxHops = 20;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
    {
        var response = await base.SendAsync(request, cancel);
        // The request the caller passed in is the caller's to dispose, so only the ones built here
        // are. The last one is left alone: the response still carries it, and a download reads the
        // host it landed on to tell a sign in page from a file.
        HttpRequestMessage? built = null;
        for (var hop = 0; hop < maxHops; hop++)
        {
            if (Next(request, response) is not { } next)
            {
                return response;
            }

            response.Dispose();
            built?.Dispose();
            built = next;
            request = next;
            response = await base.SendAsync(request, cancel);
        }

        // Out of hops, so the redirect itself is the answer. Reported as the failure it is by
        // whoever asked for the call, rather than thrown from here as a transport error.
        return response;
    }

    /// <summary>
    /// The request that follows <paramref name="response"/>, or null where nothing does: not a
    /// redirect, no Location to follow, a scheme that is not HTTP, or a body that would have to be
    /// sent twice.
    /// </summary>
    static HttpRequestMessage? Next(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (!Redirects(response.StatusCode) ||
            response.Headers.Location is not { } location)
        {
            return null;
        }

        var from = request.RequestUri!;
        var target = new Uri(from, location);
        // A Location of mailto: or file: is not something to fetch, and following one would hand
        // the credential to whatever answered it.
        if (target.Scheme != Uri.UriSchemeHttp &&
            target.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var method = MethodFor(request.Method, response.StatusCode);
        // The body has already been read by the transport and cannot be sent again, and buffering
        // every request against a redirect no CI service sends would cost every call. Only 307 and
        // 308 keep the method, so they are the only ones this can refuse, and no provider is
        // redirected on a request that carries one.
        if (method == request.Method &&
            request.Content is not null)
        {
            return null;
        }

        var next = new HttpRequestMessage(method, target);
        foreach (var (name, values) in request.Headers)
        {
            next.Headers.TryAddWithoutValidation(name, values);
        }

        if (!CredentialHosts.Follows(from, target))
        {
            foreach (var name in Credential.Headers)
            {
                next.Headers.Remove(name);
            }
        }

        return next;
    }

    /// <summary>
    /// What the transport does, and what every browser does: 307 and 308 repeat the request as it
    /// was, and the older three are followed with a GET whatever they answered, which is why a
    /// redirected POST arrives without its body.
    /// </summary>
    static HttpMethod MethodFor(HttpMethod method, HttpStatusCode status)
    {
        if (status is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
        {
            return method;
        }

        if (method == HttpMethod.Head)
        {
            return method;
        }

        return HttpMethod.Get;
    }

    static bool Redirects(HttpStatusCode status) =>
        status is
            HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
}
