/// <summary>
/// The service asked for a pause. <see cref="RetryAfter"/> is how long, from the server's own
/// header when it sent one.
/// </summary>
sealed class RateLimitException(TimeSpan retryAfter) : Exception($"Rate limited. Retry after {retryAfter}")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
