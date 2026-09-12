/// <summary>
/// Doubling from the poll interval, capped at ten minutes: a service that is down does not need
/// to be told so every thirty seconds.
/// </summary>
static class Backoff
{
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(10);

    public static TimeSpan Next(TimeSpan interval, int failures)
    {
        var exponent = Math.Clamp(failures - 1, 0, 10);
        var seconds = interval.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(Max.TotalSeconds, Math.Max(5, seconds)));
    }
}
