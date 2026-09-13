/// <summary>
/// The loop for one connection: wait, discover, fetch, apply. It never throws out; every
/// failure becomes a health on the connection, and how long it waits before the next attempt
/// depends on which failure it was.
/// </summary>
sealed class ConnectionPoller
{
    readonly string connectionId;
    readonly SessionHost host;
    readonly ISecretStore secrets;
    readonly DurationHistory history;
    readonly HttpMessageHandler handler;
    readonly TokenRefresher? refresher;
    readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    readonly CancelSource stop = new();
    readonly HashSet<string> recorded = [];
    readonly ETagCache etags = new();
    Task? loop;
    ImmutableArray<Pipeline> pipelines = [];
    DateTimeOffset discovered = DateTimeOffset.MinValue;
    int failures;

    /// <summary>
    /// How often the pipeline list is re-read. New pipelines are rare and discovery is the
    /// expensive half of a poll on the providers that need a call per repository.
    /// </summary>
    public static readonly TimeSpan RediscoverAfter = TimeSpan.FromMinutes(10);

    public const int PerPipeline = 5;

    public ConnectionPoller(string connectionId, SessionHost host, ISecretStore secrets, DurationHistory history, HttpMessageHandler handler, TokenRefresher? refresher)
    {
        this.connectionId = connectionId;
        this.host = host;
        this.secrets = secrets;
        this.history = history;
        this.handler = handler;
        this.refresher = refresher;
    }

    public void Start(Cancel cancel)
    {
        var linked = CancelSource.CreateLinkedTokenSource(cancel, stop.Token);
        loop = Task.Run(() => Run(linked.Token), Cancel.None);
    }

    public void Wake() =>
        wake.Writer.TryWrite(true);

    public void Stop() =>
        stop.Cancel();

    public Task WaitForExit() =>
        loop ?? Task.CompletedTask;

    async Task Run(Cancel cancel)
    {
        // The first poll happens at once; a tray with stale rows at startup is a tray that
        // looks broken.
        while (!cancel.IsCancellationRequested)
        {
            var health = await PollOnce(cancel);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            var delay = Delay(health);
            try
            {
                await WaitFor(delay, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The interval, or a wake, whichever is first. A sign in required waits for the wake alone:
    /// nothing changes about a dead token on its own.
    /// </summary>
    async Task WaitFor(TimeSpan? delay, Cancel cancel)
    {
        // Drain a wake that arrived during the poll, so it does not fire a second poll at once.
        while (wake.Reader.TryRead(out _))
        {
        }

        if (delay is null)
        {
            await wake.Reader.ReadAsync(cancel);
            return;
        }

        using var timeout = CancelSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(delay.Value);
        try
        {
            await wake.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // The interval elapsed.
        }
    }

    TimeSpan? Delay(ConnectionHealth health)
    {
        var state = host.State;
        var settings = state.Settings;
        switch (health)
        {
            case ConnectionHealth.NeedsAuth:
                return null;
            case ConnectionHealth.RateLimited:
            {
                var retryAfter = state.Connection(connectionId)?.RetryAfter;
                var until = retryAfter is null ? TimeSpan.FromMinutes(5) : retryAfter.Value - DateTimeOffset.UtcNow;
                return until < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : until;
            }
            case ConnectionHealth.Error:
                return Backoff.Next(TimeSpan.FromSeconds(settings.PollIntervalSeconds), failures);
            default:
            {
                var active = state.Builds.Any(_ => _.ConnectionId == connectionId && _.IsActive);
                var seconds = active ? settings.RunningPollIntervalSeconds : settings.PollIntervalSeconds;
                var connection = state.Connection(connectionId)?.Connection;
                // Bitbucket allows a thousand requests an hour and charges one per repository.
                if (connection?.ProviderId == ProviderDescriptors.Bitbucket.Id)
                {
                    seconds = Math.Max(seconds, 60);
                }

                return TimeSpan.FromSeconds(Math.Max(5, seconds));
            }
        }
    }

    public async Task<ConnectionHealth> PollOnce(Cancel cancel)
    {
        var connection = host.State.Connection(connectionId)?.Connection;
        if (connection is null)
        {
            return ConnectionHealth.Error;
        }

        var provider = Providers.Get(connection.ProviderId);
        var secret = secrets.Read(SecretKeys.Token(connection.Id));
        if (secret is null)
        {
            Set(ConnectionHealth.NeedsAuth, "No credential stored");
            return ConnectionHealth.NeedsAuth;
        }

        host.Mutate(_ => MonitorSession.SetHealth(_, connectionId, ConnectionHealth.Polling));
        try
        {
            return await Poll(provider, connection, secret, cancel);
        }
        catch (AuthException exception)
        {
            if (refresher is not null &&
                await refresher.TryRefresh(connection, cancel))
            {
                try
                {
                    return await Poll(provider, connection, secrets.Read(SecretKeys.Token(connection.Id))!, cancel);
                }
                catch (AuthException again)
                {
                    Set(ConnectionHealth.NeedsAuth, again.Message);
                    return ConnectionHealth.NeedsAuth;
                }
            }

            Set(ConnectionHealth.NeedsAuth, exception.Message);
            return ConnectionHealth.NeedsAuth;
        }
        catch (RateLimitException exception)
        {
            Set(ConnectionHealth.RateLimited, exception.Message, DateTimeOffset.UtcNow + exception.RetryAfter);
            return ConnectionHealth.RateLimited;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return ConnectionHealth.Error;
        }
        catch (Exception exception)
        {
            failures++;
            Log.Warning(exception, "Polling {Connection} failed", connection.Name);
            Set(ConnectionHealth.Error, exception.Message);
            return ConnectionHealth.Error;
        }
    }

    async Task<ConnectionHealth> Poll(IProvider provider, Connection connection, string secret, Cancel cancel)
    {
        var context = Providers.Context(connection, secret, handler, etags) with
        {
            Progress = progress => host.Mutate(_ => MonitorSession.SetProgress(_, connectionId, progress))
        };
        var now = DateTimeOffset.UtcNow;
        if (pipelines.Length == 0 ||
            now - discovered > RediscoverAfter)
        {
            var found = await provider.DiscoverPipelines(context, cancel);
            var filters = host.State.Settings.Filters;
            pipelines = [..found.Where(_ => !Filters.ExcludesPipeline(filters, _))];
            discovered = now;
            // Every URL still in use is requested at least once between discoveries.
            etags.Rotate();
        }

        var builds = await provider.FetchBuilds(context, pipelines, PerPipeline, cancel);
        RecordDurations(builds);
        var medians = history.Medians();
        host.Mutate(_ => MonitorSession.ApplyMedians(MonitorSession.ApplyPoll(_, connectionId, pipelines, [..builds], DateTimeOffset.UtcNow), medians));
        failures = 0;
        return ConnectionHealth.Ok;
    }

    /// <summary>
    /// Every finished successful run is recorded once. Its own timestamps say how long it took;
    /// no state about what was running last time is needed.
    /// </summary>
    void RecordDurations(IReadOnlyList<Build> builds)
    {
        foreach (var build in builds)
        {
            if (build.Status != BuildStatus.Succeeded ||
                build.Started is null ||
                build.Finished is null)
            {
                continue;
            }

            var key = $"{build.PipelineKey}/{build.RunNumber}/{build.ProviderRef}";
            if (recorded.Add(key))
            {
                history.Record(build.PipelineKey, build.Finished.Value - build.Started.Value);
            }
        }
    }

    void Set(ConnectionHealth health, string? error, DateTimeOffset? retryAfter = null) =>
        host.Mutate(_ => MonitorSession.SetHealth(_, connectionId, health, error, retryAfter));
}
