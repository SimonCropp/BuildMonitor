/// <summary>
/// How long a build is expected to take. The provider's own number wins when it has one;
/// otherwise the median of the pipeline's recent successful runs.
/// </summary>
static class Estimator
{
    public static TimeSpan? Estimate(Build build, ImmutableDictionary<string, TimeSpan> medians)
    {
        if (build.Estimate?.Duration is { } duration)
        {
            return duration;
        }

        if (medians.TryGetValue(build.PipelineKey, out var median))
        {
            return median;
        }

        return null;
    }

    /// <summary>
    /// The stretch of its run in which a build is expected to finish. The provider's own duration
    /// wins, as it does for <see cref="Estimate"/>, from three quarters of it to all of it, since one
    /// number says nothing of how runs vary; otherwise the fastest to the slowest of the pipeline's
    /// recent successful runs.
    /// </summary>
    public static DurationRange? Window(Build build, ImmutableDictionary<string, DurationRange> ranges)
    {
        if (build.Estimate?.Duration is { } duration)
        {
            return new(duration * 0.75, duration);
        }

        if (ranges.TryGetValue(build.PipelineKey, out var range))
        {
            return range;
        }

        return null;
    }
}
