/// <summary>
/// The runtime half of a <see cref="Settings"/> connection: how the last poll went.
/// </summary>
record ConnectionState(
    Connection Connection,
    ConnectionHealth Health,
    string? Error,
    DateTimeOffset? LastPolled,
    ImmutableArray<Pipeline> Pipelines,
    DateTimeOffset? RetryAfter)
{
    public static ConnectionState Start(Connection connection) =>
        new(connection, ConnectionHealth.Unpolled, null, null, [], null);

    public string Describe(DateTimeOffset now) =>
        Health switch
        {
            ConnectionHealth.Unpolled => "not polled yet",
            ConnectionHealth.Polling => "polling",
            ConnectionHealth.Error => $"error: {Error}",
            ConnectionHealth.NeedsAuth => "sign in required",
            ConnectionHealth.RateLimited => RetryAfter is null
                ? "rate limited"
                : $"rate limited, retrying in {Progress.Age(RetryAfter.Value - now)}",
            _ => ""
        };
}
