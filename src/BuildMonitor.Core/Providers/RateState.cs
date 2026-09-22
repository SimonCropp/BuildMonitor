/// <summary>
/// What a connection knows about its rate limit, folded from every response.
/// <para>
/// Responses to concurrent requests arrive out of order, so within one reset window the lowest
/// remaining count wins; a later reset starts a new window, and an earlier one is a straggler from
/// the last.
/// </para>
/// <para>
/// Azure DevOps sends Retry-After on a successful response when a user nears its limit, before it
/// starts delaying requests. Ignored, the next requests are the ones it delays or blocks, so here
/// it becomes a pause.
/// </para>
/// </summary>
record RateState(double? Limit, double? Remaining, DateTimeOffset? Reset, bool NearLimit, DateTimeOffset? PausedUntil, DateTimeOffset? Observed)
{
    public static readonly RateState Unknown = new(null, null, null, false, null, null);

    /// <summary>
    /// Resets for one window reported by different servers differ by a second or so.
    /// </summary>
    static TimeSpan sameWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a count without a reset is trusted. Azure DevOps measures its limit over a sliding
    /// five minutes and stops sending the count once usage falls, so a count held for longer would
    /// hold a connection back after the pressure had gone.
    /// </summary>
    static TimeSpan slidingWindow = TimeSpan.FromMinutes(5);

    public RateState Merge(RateObservation observation, bool success, DateTimeOffset now)
    {
        var next = this with
        {
            Limit = observation.Limit ?? Limit,
            NearLimit = observation.NearLimit ?? NearLimit
        };

        if (observation.Remaining is { } remaining &&
            !Straggler(observation.Reset))
        {
            next = next with
            {
                Remaining = SameWindow(observation.Reset) && Remaining is { } known ? Math.Min(known, remaining) : remaining,
                Reset = observation.Reset,
                Observed = now
            };
        }

        if (success &&
            observation.RetryAfter is { } retryAfter &&
            (PausedUntil is null || now + retryAfter > PausedUntil))
        {
            next = next with
            {
                PausedUntil = now + retryAfter
            };
        }

        return next;
    }

    /// <summary>
    /// Whether the count still describes the window it came from.
    /// </summary>
    public bool Current(DateTimeOffset now)
    {
        if (Remaining is null)
        {
            return false;
        }

        if (Reset is { } reset)
        {
            return reset > now;
        }

        return Observed is { } observed && now - observed < slidingWindow;
    }

    bool SameWindow(DateTimeOffset? reset) =>
        reset is { } incoming &&
        Reset is { } current &&
        (incoming - current).Duration() <= sameWindow;

    bool Straggler(DateTimeOffset? reset) =>
        reset is { } incoming &&
        Reset is { } current &&
        incoming < current - sameWindow;
}
