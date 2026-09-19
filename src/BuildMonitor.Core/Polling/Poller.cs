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
    // One for every connection: a name learnt from one service names the rows of another that was
    // handed only the id. See IdentityNames.
    IdentityNames identities = new();

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

            var poller = new ConnectionPoller(connection.Id, host, secrets, history, handler, refresher, clock, identities);
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

    /// <summary>
    /// Runs <paramref name="work"/> against the provider and a context for the build's connection.
    /// Collecting artifacts is a listing and then a download each, and one context for all of them
    /// costs one client rather than one per file.
    /// </summary>
    public Task<T> WithProvider<T>(Build build, Func<IProvider, ProviderContext, Task<T>> work)
    {
        var connection = Connection(build.ConnectionId);
        return work(Providers.Get(connection.ProviderId), Context(connection));
    }

    /// <summary>
    /// <see cref="WithProvider{T}"/> for work that answers nothing, so a caller that only writes
    /// files does not have to invent a return value for it.
    /// </summary>
    public Task WithProvider(Build build, Func<IProvider, ProviderContext, Task> work)
    {
        var connection = Connection(build.ConnectionId);
        return work(Providers.Get(connection.ProviderId), Context(connection));
    }

    /// <summary>
    /// What the build's service is called, for a message that has to say which of them could not
    /// be asked for something.
    /// </summary>
    public ProviderDescriptor Descriptor(Build build) =>
        ProviderDescriptors.Get(Connection(build.ConnectionId).ProviderId);

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
