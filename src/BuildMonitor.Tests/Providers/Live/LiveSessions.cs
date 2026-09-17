/// <summary>
/// Access and discovery, done once per provider for all the read tests.
/// <para>
/// Discovering a GitHub account costs a listing plus a request per repository. Each test
/// repeating it would spend the quota the fetch tests measure.
/// </para>
/// <para>
/// Discovery runs under its own deadline, not the first caller's. Otherwise one test timing out
/// would fail every other test waiting on the same discovery.
/// </para>
/// </summary>
static class LiveSessions
{
    static ConcurrentDictionary<string, Lazy<Task<LiveSession>>> sessions = new();

    public static Task<LiveSession> Get(LiveConnection live, Cancel cancel) =>
        sessions
            .GetOrAdd(live.Id, _ => new(() => Discover(live)))
            .Value
            .WaitAsync(cancel);

    static async Task<LiveSession> Discover(LiveConnection live)
    {
        using var deadline = new CancelSource(TimeSpan.FromMinutes(10));
        var cancel = deadline.Token;
        var memory = new ProviderMemory();
        var access = await Access(live, memory, cancel);
        var pipelines = await live.Provider.DiscoverPipelines(live.Context(memory, access: access), cancel);
        var (sandbox, problem) = LiveSandbox.Find(live, pipelines);
        return new(live, memory, access, pipelines, sandbox, problem);
    }

    /// <summary>
    /// As the poller asks it: any failure other than a refused token or a rate limit leaves access
    /// unknown rather than failing discovery. SignIn asks again and fails on its own.
    /// </summary>
    static async Task<BuildAccess> Access(LiveConnection live, ProviderMemory memory, Cancel cancel)
    {
        try
        {
            return await live.Provider.Access(live.Context(memory), cancel);
        }
        catch (Exception exception) when (exception is not (RateLimitException or AuthException { Status: HttpStatusCode.Unauthorized } or OperationCanceledException))
        {
            LiveLog.Warning($"{live.Id}: asking what the token may do failed with {exception.GetType().Name}");
            return BuildAccess.Unknown;
        }
    }
}
