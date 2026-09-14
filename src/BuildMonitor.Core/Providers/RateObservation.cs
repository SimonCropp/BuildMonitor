/// <summary>
/// What one response said about the rate limit. Every field is optional because every service
/// sends a different subset, and most send nothing at all.
/// </summary>
record RateObservation(double? Limit, double? Remaining, DateTimeOffset? Reset, bool? NearLimit, double? Cost, TimeSpan? RetryAfter)
{
    public static readonly RateObservation None = new(null, null, null, null, null, null);
}
