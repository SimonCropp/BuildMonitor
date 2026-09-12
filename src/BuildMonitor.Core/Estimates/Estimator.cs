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
}
