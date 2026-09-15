/// <summary>
/// The bar and the text beside it, from one build and the clock.
/// </summary>
static class Progress
{
    /// <summary>
    /// A running build never reaches the end of its bar: the estimate is a guess, and a bar that
    /// fills and then sits there full reads as finished.
    /// </summary>
    const double runningCap = 0.95;

    public static (double Fraction, string Text) Compute(Build build, TimeSpan? estimate, DateTimeOffset now)
    {
        switch (build.Status)
        {
            case BuildStatus.Queued:
                return (-1, "queued");
            case BuildStatus.Running:
                return Running(build, estimate, now);
            default:
                var finished = build.Finished ?? build.Started ?? build.Queued;
                return (-1, finished is null ? "" : $"{Age(now - finished.Value)} ago");
        }
    }

    static (double, string) Running(Build build, TimeSpan? estimate, DateTimeOffset now)
    {
        var started = build.Started ?? build.Queued ?? now;
        var elapsed = now - started;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (build.Estimate?.Remaining is { } remaining)
        {
            return (Cap(RemainingFraction(build.Estimate.Percent, elapsed, remaining)), Countdown(remaining));
        }

        if (build.Estimate?.Percent is { } onlyPercent)
        {
            return (Cap(onlyPercent / 100), Format(elapsed));
        }

        if (estimate is null ||
            estimate.Value <= TimeSpan.Zero)
        {
            return (-1, Format(elapsed));
        }

        return (Cap(elapsed / estimate.Value), Countdown(estimate.Value - elapsed));
    }

    /// <summary>
    /// The provider's percent when it gives one, otherwise the share of elapsed plus remaining that
    /// has gone. Nothing left, or an overrun, is as full as a running bar gets: worked out, a build
    /// with nothing elapsed and nothing left is 0/0, which is NaN rather than an error and passes
    /// through the clamp, and an overrun on a short run went negative and emptied the bar.
    /// </summary>
    static double RemainingFraction(double? percent, TimeSpan elapsed, TimeSpan remaining)
    {
        if (percent is { } value)
        {
            return value / 100;
        }

        if (remaining <= TimeSpan.Zero)
        {
            return runningCap;
        }

        return 1 - remaining / (elapsed + remaining);
    }

    static double Cap(double fraction) =>
        Math.Clamp(fraction, 0, runningCap);

    static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            return $"+{Format(-remaining)}";
        }

        return $"{Format(remaining)} left";
    }

    public static string Format(TimeSpan span)
    {
        span = TimeSpan.FromSeconds(Math.Floor(span.TotalSeconds));
        if (span.TotalHours >= 1)
        {
            return $"{(int) span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        }

        return $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string Age(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(0, (int) span.TotalSeconds)}s";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int) span.TotalMinutes}m";
        }

        if (span < TimeSpan.FromDays(1))
        {
            return $"{(int) span.TotalHours}h";
        }

        return $"{(int) span.TotalDays}d";
    }
}
