/// <summary>
/// One <see cref="ConnectionPoller"/> per connection, kept in step with the settings. Every
/// result lands in the <see cref="SessionHost"/>; nothing here holds state of its own beyond
/// which loops are running.
/// </summary>
sealed class Poller(
    SessionHost host,
    ISecretStore secrets,
    DurationHistory history,
    HttpMessageHandler handler,
    TokenRefresher? refresher = null,
    Func<DateTimeOffset>? clock = null)
    : IAsyncDisposable
{
    ConcurrentDictionary<string, ConnectionPoller> pollers = new();
    CancelSource cancel = new();

    public void Start() =>
        Sync(host.State.Settings);

    /// <summary>
    /// Starts a loop for every connection that has none and stops the ones that are gone.
    /// Called after every save, so the options page is the only thing that decides what runs.
    /// </summary>
    public void Sync(Settings settings)
    {
        var wanted = settings.Connections.Select(_ => _.Id).ToHashSet();
        foreach (var (id, poller) in pollers)
        {
            if (!wanted.Contains(id))
            {
                pollers.TryRemove(id, out _);
                poller.Stop();
            }
        }

        foreach (var connection in settings.Connections)
        {
            if (pollers.TryGetValue(connection.Id, out var existing))
            {
                // A changed interval or filter applies from the next plan, not after the current sleep.
                existing.Wake();
                continue;
            }

            var poller = new ConnectionPoller(connection.Id, host, secrets, history, handler, refresher, clock);
            if (pollers.TryAdd(connection.Id, poller))
            {
                poller.Start(cancel.Token);
            }
        }
    }

    public void Refresh(string? connectionId)
    {
        if (connectionId is null)
        {
            foreach (var poller in pollers.Values)
            {
                poller.Refresh();
            }

            return;
        }

        if (pollers.TryGetValue(connectionId, out var one))
        {
            one.Refresh();
        }
    }

    /// <summary>
    /// After a retry or a cancel only that build's group needs fetching, soon and for a few minutes
    /// after; a refresh would fetch every group for one change. A poller that never started, as in
    /// some tests, has nothing to nudge.
    /// </summary>
    void Nudge(Build build)
    {
        if (pollers.TryGetValue(build.ConnectionId, out var poller))
        {
            poller.Nudge(build.PipelineId);
        }
    }

    public ProviderContext Context(Connection connection, string? token = null) =>
        Providers.Context(connection, token ?? secrets.Read(SecretKeys.Token(connection.Id)), handler);

    public async Task Retry(Build build, Cancel token)
    {
        var connection = Connection(build.ConnectionId);
        await Providers.Get(connection.ProviderId).Retry(Context(connection), build, token);
        Nudge(build);
    }

    public async Task Cancel(Build build, Cancel token)
    {
        var connection = Connection(build.ConnectionId);
        await Providers.Get(connection.ProviderId).Cancel(Context(connection), build, token);
        Nudge(build);
    }

    public Task<string> FetchLog(Build build, Cancel token)
    {
        var connection = Connection(build.ConnectionId);
        return Providers.Get(connection.ProviderId).FetchLog(Context(connection), build, token);
    }

    public Task<ConnectionTest> Test(Connection connection, string? token, Cancel cancelToken) =>
        Providers.Get(connection.ProviderId).Test(Context(connection, token), cancelToken);

    Connection Connection(string id) =>
        host.State.Connection(id)?.Connection ??
        throw new InvalidOperationException($"No connection {id}");

    public async ValueTask DisposeAsync()
    {
        await cancel.CancelAsync();
        foreach (var poller in pollers.Values)
        {
            await poller.WaitForExit();
        }

        cancel.Dispose();
    }
}
