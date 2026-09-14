/// <summary>
/// The ETag and body of every GET that carried one, kept for as long as a connection is polled
/// so the next request for the same URL can be conditional.
/// <para>
/// A new <see cref="HttpJson"/> is built for every poll, so a cache it owned would start empty
/// each time and never send If-None-Match. Keeping every entry forever would grow without end,
/// because URLs come and go with runs: GitLab fetches each pipeline by id. So entries live in two
/// generations, and <see cref="Rotate"/> drops whatever was not requested since the call before.
/// The poller rotates on a clock, slower than the longest interval a group can wait, so a quiet or
/// backed off group keeps its entry.
/// </para>
/// </summary>
sealed class ETagCache
{
    ConcurrentDictionary<string, (string ETag, byte[] Body)> current = new();
    ConcurrentDictionary<string, (string ETag, byte[] Body)> previous = new();

    public bool TryGet(string url, out (string ETag, byte[] Body) entry)
    {
        if (current.TryGetValue(url, out entry))
        {
            return true;
        }

        if (!previous.TryRemove(url, out entry))
        {
            return false;
        }

        current[url] = entry;
        return true;
    }

    public void Set(string url, string etag, byte[] body) =>
        current[url] = (etag, body);

    /// <summary>
    /// Whether a URL is cached, without keeping its entry alive: a probe choosing between two
    /// listings must not hold on to the one it decided against.
    /// </summary>
    public bool Contains(string url) =>
        current.ContainsKey(url) || previous.ContainsKey(url);

    public void Rotate()
    {
        previous = current;
        current = new();
    }
}
