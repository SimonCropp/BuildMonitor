/// <summary>
/// The oldest a finished build may be and still be shown: the start of the UTC day that many days
/// back. A day rather than the exact moment, because the date goes into request URLs, and a date
/// that moved on every poll would miss every ETag cached against the URL before it.
/// </summary>
static class HistoryCutoff
{
    public static DateTimeOffset Of(DateTimeOffset now, int days) =>
        new(now.UtcDateTime.Date.AddDays(-days), TimeSpan.Zero);

    /// <summary>
    /// A running or queued build is kept whatever its age, as is one with no time to judge by.
    /// </summary>
    public static bool Keeps(Build build, DateTimeOffset cutoff) =>
        build.IsActive ||
        (build.Finished ?? build.Ordering) is not { } at ||
        at >= cutoff;
}
