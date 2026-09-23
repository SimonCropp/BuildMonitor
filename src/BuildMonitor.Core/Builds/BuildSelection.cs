/// <summary>
/// Which of a pipeline's runs become rows: its own run, and, when asked, the newest run of each
/// other branch that is running, queued or failed. A passing run on a feature branch is history; a
/// running one is something to watch, and a failed one something to read. Each other branch is
/// judged by its most recent run rather than any active one: GitHub can leave a run queued for days
/// after later runs on that branch finished, and that ghost otherwise sat grey on the screen over
/// the green run.
/// <para>
/// A pipeline's own run is its newest on its default branch rather than its newest on any: a pull
/// request's run took the row whenever it was the newer, so a green one hid a red main and a red
/// one painted main red. Where the provider knows no default branch, or no run on it is held, the
/// newest run on any branch stands for the pipeline, as it always did.
/// </para>
/// </summary>
static class BuildSelection
{
    public static ImmutableArray<PipelineBuilds> Select(IEnumerable<Build> builds, bool showOtherBranches)
    {
        var result = ImmutableArray.CreateBuilder<PipelineBuilds>();
        // By the parts of the pipeline key, which would otherwise be built for every build on every
        // projection of the rows.
        foreach (var pipeline in builds.GroupBy(_ => (_.ConnectionId, _.PipelineId)))
        {
            var ordered = pipeline
                .OrderByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
                .ToList();
            var head = HeadOf(ordered);
            if (!showOtherBranches)
            {
                result.Add(new(head, [], []));
                continue;
            }

            var lanes = new List<Build>();
            var folded = ImmutableArray.CreateBuilder<Build>();
            foreach (var branch in ordered
                         .Where(_ => _.Branch != head.Branch)
                         .GroupBy(_ => _.Branch))
            {
                var lane = LaneOf(branch.ToList());
                if (lane.NeedsAttention())
                {
                    lanes.Add(lane);
                }
                else
                {
                    folded.Add(lane);
                }
            }

            result.Add(
                new(
                    head,
                    [
                        ..lanes
                            .OrderBy(_ => _.Rank())
                            .ThenByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
                            .ThenBy(_ => _.Key, StringComparer.Ordinal)
                    ],
                    folded.ToImmutable()));
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// The newest run on the default branch the runs carry, or the newest run where they carry none
    /// or none of them is on it.
    /// </summary>
    static Build HeadOf(List<Build> ordered)
    {
        if (ordered.Select(_ => _.DefaultBranch).OfType<string>().FirstOrDefault() is { } defaultBranch &&
            ordered.FirstOrDefault(_ => _.Branch == defaultBranch) is { } own)
        {
            return own;
        }

        return ordered[0];
    }

    /// <summary>
    /// The branch's newest run, carrying the pull request an older run of it named where it names
    /// none itself. AppVeyor builds a pull request's branch and then the pull request, two runs of
    /// one branch of which only one knows the pull request, and the lane would lose its PR button
    /// whenever the other was the newer.
    /// </summary>
    static Build LaneOf(List<Build> runs)
    {
        var newest = runs[0];
        if (newest.PullRequestNumber is not null ||
            runs.FirstOrDefault(_ => _.PullRequestNumber is not null) is not { } named)
        {
            return newest;
        }

        return newest with
        {
            PullRequestNumber = named.PullRequestNumber,
            PullRequestUrl = named.PullRequestUrl
        };
    }
}
