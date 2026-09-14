/// <summary>
/// The service asked for a pause. <see cref="RetryAfter"/> is how long, from the service's own
/// headers, or null when it named no time and the caller has to back off on its own.
/// </summary>
sealed class RateLimitException(TimeSpan? retryAfter) :
    Exception(retryAfter is null ? "Rate limited" : $"Rate limited. Retry after {retryAfter}")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
