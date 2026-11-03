/// <summary>
/// Reads the rate limit headers of every provider into one shape.
/// <para>
/// GitHub and Azure DevOps send X-RateLimit-*, GitLab sends RateLimit-*. Bitbucket lists windows
/// in its limit, <c>60, 60;w=3600</c>, and counts its reset in seconds left where the others send
/// a Unix time. Read as a Unix time, Bitbucket's reset lands in 1970, the wait comes out negative,
/// and a limited connection is retried at once.
/// </para>
/// </summary>
static class RateHeaders
{
    /// <summary>
    /// The smallest reset read as a Unix time, which is in 2001; anything smaller is seconds left.
    /// </summary>
    const double unixTimeFloor = 1_000_000_000;

    const double unixTimeCeiling = 253_402_300_799;

    public static RateObservation Read(HttpResponseHeaders headers, DateTimeOffset now)
    {
        var limit = Number(headers, "X-RateLimit-Limit", "RateLimit-Limit");
        var remaining = Number(headers, "X-RateLimit-Remaining", "RateLimit-Remaining");
        var reset = Reset(Number(headers, "X-RateLimit-Reset", "RateLimit-Reset"), now);
        bool? nearLimit = First(headers, "X-RateLimit-NearLimit") is { } near
            ? bool.TryParse(near, out var parsed) && parsed
            : null;
        var cost = Number(headers, "X-RateLimit-Cost");
        return new(limit, remaining, reset, nearLimit, cost, RetryAfter(headers, now));
    }

    /// <summary>
    /// Whether the response counts against a limit of its own rather than the connection's. GitHub
    /// names the bucket in X-RateLimit-Resource, and its search API's is thirty a minute: merged into
    /// the hourly five thousand, its 29 left read as a connection all but out of requests.
    /// </summary>
    public static bool SeparateLimit(HttpResponseHeaders headers) =>
        First(headers, "X-RateLimit-Resource") is { } resource &&
        !resource.Equals("core", StringComparison.OrdinalIgnoreCase);

    static DateTimeOffset? Reset(double? value, DateTimeOffset now) =>
        value switch
        {
            null or < 0 or > unixTimeCeiling => null,
            < unixTimeFloor => now + TimeSpan.FromSeconds(value.Value),
            _ => DateTimeOffset.FromUnixTimeSeconds((long) value.Value)
        };

    static TimeSpan? RetryAfter(HttpResponseHeaders headers, DateTimeOffset now)
    {
        if (headers.RetryAfter is not { } header)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            if (delta > TimeSpan.Zero)
            {
                return delta;
            }

            return null;
        }

        if (header.Date is { } date &&
            date > now)
        {
            return date - now;
        }

        return null;
    }

    static double? Number(HttpResponseHeaders headers, params string[] names)
    {
        foreach (var name in names)
        {
            if (First(headers, name) is not { } text)
            {
                continue;
            }

            var span = text.AsSpan();
            var end = span.IndexOfAny(',', ';');
            var number = end < 0 ? span : span[..end];
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    static string? First(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }
}
