/// <summary>
/// The rate limit state of one connection, recorded from every response on its token, 304s
/// included, and a count of the requests sent.
/// <para>
/// A new <see cref="HttpJson"/> is built for every poll, so a budget it owned would forget
/// everything between polls; like <see cref="ETagCache"/> it belongs to the poller. It is locked
/// because GitHub fetches eight repositories at once.
/// </para>
/// </summary>
sealed class RateBudget(Func<DateTimeOffset> clock)
{
    Lock gate = new();
    RateState state = RateState.Unknown;
    long sent;
    double cost;

    public RateState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public long SentCount => Interlocked.Read(ref sent);

    /// <summary>
    /// The sum of Azure DevOps' X-RateLimit-Cost, in throughput units rather than requests.
    /// </summary>
    public double CostTotal
    {
        get
        {
            lock (gate)
            {
                return cost;
            }
        }
    }

    public void Sent() =>
        Interlocked.Increment(ref sent);

    public void Record(HttpResponseMessage response)
    {
        var now = clock();
        var observation = RateHeaders.Read(response.Headers, now);
        lock (gate)
        {
            state = state.Merge(observation, (int) response.StatusCode < 400, now);
            cost += observation.Cost ?? 0;
        }
    }
}
