/// <summary>
/// Counts what went over the wire. A request sent without the ETag it could have carried costs a
/// service's quota, and nothing on screen shows it.
/// <para>
/// It forwards through an invoker that does not own the shared handler. Disposing a
/// DelegatingHandler would also dispose the handler every other live test uses.
/// </para>
/// </summary>
sealed class CountingHandler(HttpMessageHandler inner) :
    HttpMessageHandler
{
    HttpMessageInvoker invoker = new(inner, disposeHandler: false);
    int requests;
    int notModified;

    public int Requests => requests;

    public int NotModified => notModified;

    /// <summary>
    /// Every URL asked for with GET.
    /// </summary>
    public ConcurrentDictionary<string, bool> Requested { get; } = new();

    /// <summary>
    /// Every URL asked for with If-None-Match.
    /// </summary>
    public ConcurrentDictionary<string, bool> Revalidated { get; } = new();

    /// <summary>
    /// Every URL whose answer carried an ETag.
    /// </summary>
    public ConcurrentDictionary<string, bool> Tagged { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
    {
        // Read before sending: following a redirect rewrites the request's URI.
        var url = request.RequestUri!.ToString();
        var get = request.Method == HttpMethod.Get;
        Interlocked.Increment(ref requests);
        if (get)
        {
            Requested[url] = true;
        }

        if (request.Headers.IfNoneMatch.Count > 0)
        {
            Revalidated[url] = true;
        }

        var response = await invoker.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            Interlocked.Increment(ref notModified);
        }

        if (get &&
            response.Headers.ETag is not null)
        {
            Tagged[url] = true;
        }

        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            invoker.Dispose();
        }

        base.Dispose(disposing);
    }
}
