/// <summary>
/// The runtime half of a <see cref="Settings"/> connection: how the last poll went.
/// </summary>
record ConnectionState(
    Connection Connection,
    ConnectionHealth Health,
    string? Error,
    DateTimeOffset? LastPolled,
    ImmutableArray<Pipeline> Pipelines,
    DateTimeOffset? RetryAfter,
    // How far the poll in progress has got, when the provider counts. Null between polls.
    PollProgress? Progress = null)
{
    public static ConnectionState Start(Connection connection) =>
        new(connection, ConnectionHealth.Unpolled, null, null, [], null);

    public string Describe(DateTimeOffset now) =>
        Health switch
        {
            ConnectionHealth.Unpolled => "not polled yet",
            ConnectionHealth.Polling => Progress is { } progress ? $"polling {progress.Done}/{progress.Total}" : "polling",
            ConnectionHealth.Error => $"error: {Error}",
            ConnectionHealth.NeedsAuth => "sign in required",
            ConnectionHealth.RateLimited => RetryAfter is null
                ? "rate limited"
                : $"rate limited, retrying in {(global::Progress.Age(RetryAfter.Value - now))}",
            _ => ""
        };
}
