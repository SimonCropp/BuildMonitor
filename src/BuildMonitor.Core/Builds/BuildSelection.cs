/// <summary>
/// Which of a pipeline's runs become rows: the most recent one on any branch, plus, when asked,
/// one row per other branch whose most recent run is queued or running right now. A finished run on
/// a feature branch is history; a running one is something to watch. Judged by the branch's most
/// recent run rather than any active one: GitHub can leave a run queued for days after later runs on
/// that branch finished, and that ghost otherwise sat grey on the screen over the green run.
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
                         .Where(_ => _.Branch != latest.Branch)
                         .GroupBy(_ => _.Branch))
            {
                var newest = branch.First();
                if (newest.IsActive)
                {
                    result.Add(newest);
                }
            }
        }

        return result.ToImmutable();
    }
}
