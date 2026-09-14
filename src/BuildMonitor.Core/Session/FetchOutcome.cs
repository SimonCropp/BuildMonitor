/// <summary>
/// What one scheduled cycle produced for a connection: the pipelines as discovered now, the
/// pipelines it fetched and their builds, which of those were fetched for the first time, and the
/// health to show.
/// </summary>
record FetchOutcome(
    ImmutableArray<Pipeline> Pipelines,
    ImmutableHashSet<string> Fetched,
    ImmutableHashSet<string> FirstFetch,
    ImmutableArray<Build> Builds,
    ConnectionHealth Health,
    string? Error,
    DateTimeOffset? RetryAfter);
