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
            return delta > TimeSpan.Zero ? delta : null;
        }

        return header.Date is { } date && date > now ? date - now : null;
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
            if (double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
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
