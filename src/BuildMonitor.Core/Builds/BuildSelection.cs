/// <summary>
/// Which of a pipeline's runs become rows: the most recent one on any branch, plus, when asked,
/// one row per other branch that is queued or running right now. A finished run on a feature
/// branch is history; a running one is something to watch.
/// </summary>
static class BuildSelection
{
    public static ImmutableArray<Build> Select(IEnumerable<Build> builds, bool showOtherBranches)
    {
        var result = ImmutableArray.CreateBuilder<Build>();
        foreach (var pipeline in builds.GroupBy(_ => _.PipelineKey))
        {
            var ordered = pipeline
                .OrderByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
                .ToList();
            var latest = ordered[0];
            result.Add(latest);
            if (!showOtherBranches)
            {
                continue;
            }

            foreach (var branch in ordered
                         .Where(_ => _.IsActive && _.Branch != latest.Branch)
                         .GroupBy(_ => _.Branch))
            {
                result.Add(branch.First());
            }
        }

        return result.ToImmutable();
    }
}
