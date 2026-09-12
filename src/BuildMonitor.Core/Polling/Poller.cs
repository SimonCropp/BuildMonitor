/// <summary>
/// One <see cref="ConnectionPoller"/> per connection, kept in step with the settings. Every
/// result lands in the <see cref="SessionHost"/>; nothing here holds state of its own beyond
/// which loops are running.
/// </summary>
sealed class Poller : IAsyncDisposable
{
    readonly SessionHost host;
    readonly ISecretStore secrets;
    readonly DurationHistory history;
    readonly HttpMessageHandler handler;
    readonly TokenRefresher? refresher;
    readonly ConcurrentDictionary<string, ConnectionPoller> pollers = new();
    readonly CancelSource cancel = new();

    public Poller(SessionHost host, ISecretStore secrets, DurationHistory history, HttpMessageHandler handler, TokenRefresher? refresher = null)
    {
        this.host = host;
        this.secrets = secrets;
        this.history = history;
        this.handler = handler;
        this.refresher = refresher;
    }

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
            if (!pollers.ContainsKey(connection.Id))
            {
                var poller = new ConnectionPoller(connection.Id, host, secrets, history, handler, refresher);
                if (pollers.TryAdd(connection.Id, poller))
                {
                    poller.Start(cancel.Token);
                }
            }
        }
    }

    public void Refresh(string? connectionId)
    {
        if (connectionId is null)
        {
            foreach (var poller in pollers.Values)
            {
                poller.Wake();
            }

            return;
        }

        if (pollers.TryGetValue(connectionId, out var one))
        {
            one.Wake();
        }
    }

    public ProviderContext Context(Connection connection, string? token = null) =>
        Providers.Context(connection, token ?? secrets.Read(SecretKeys.Token(connection.Id)), handler);

    public async Task Retry(Build build, Cancel token)
    {
        var connection = Connection(build.ConnectionId);
        await Providers.Get(connection.ProviderId).Retry(Context(connection), build, token);
        Refresh(build.ConnectionId);
    }

    public async Task Cancel(Build build, Cancel token)
    {
        var connection = Connection(build.ConnectionId);
        await Providers.Get(connection.ProviderId).Cancel(Context(connection), build, token);
        Refresh(build.ConnectionId);
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
