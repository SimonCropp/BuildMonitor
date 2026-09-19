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
                return (-1, Waiting(build, now));
            case BuildStatus.Running:
                return Running(build, estimate, now);
            default:
                var finished = build.Finished ?? build.Started ?? build.Queued;
                return (-1, finished is null ? "" : $"{Age(now - finished.Value)} ago");
        }
    }

    /// <summary>
    /// How long the run has been waiting for an agent. A bare "queued" read the same after a day
    /// stuck behind an offline pool as it did a second after the run was raised, which is the one
    /// thing worth knowing about a build that has not started. Providers that report no queue time,
    /// Travis among them, keep the bare word rather than counting from an arbitrary zero.
    /// </summary>
    static string Waiting(Build build, DateTimeOffset now)
    {
        if (build.Queued is not { } queued)
        {
            return "queued";
        }

        return $"queued {Age(now - queued)}";
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

    /// <summary>
    /// The longest texts <see cref="Compute"/> can produce, for a head sizing the column they go in.
    /// A column sized from the rows on screen would step a few pixels wider every time a countdown
    /// passed an hour or a wait passed a day, moving every column beside it; sized from these it
    /// never moves, and no row is left drawing "queued 3…".
    /// </summary>
    public static readonly string[] Widest = ["00:00:00 left", "+00:00:00", "queued 000d"];

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
