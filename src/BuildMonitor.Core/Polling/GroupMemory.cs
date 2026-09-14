/// <summary>
/// What the poller remembers about one group between cycles. It is not session state: nothing on
/// screen reads it, and it starts empty whenever the poller does.
/// </summary>
/// <param name="FetchedPipelines">Pipelines fetched at least once. A new one makes its group due,
/// and its first fetch is not news, so a red pipeline is not announced on sight.</param>
/// <param name="SeenActivity">The last activity token a probe reported for the group.</param>
record GroupMemory(
    DateTimeOffset? LastAttempt,
    int Failures,
    string? Error,
    DateTimeOffset? NudgedAt,
    ImmutableHashSet<string> FetchedPipelines,
    string? SeenActivity)
{
    public static readonly GroupMemory New = new(null, 0, null, null, [], null);
}
