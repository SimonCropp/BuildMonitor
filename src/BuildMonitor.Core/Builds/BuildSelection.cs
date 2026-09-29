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
/// <para>
/// A failed branch is only worth reading while it can still be fixed. One whose pull request was
/// merged or closed, or whose branch was deleted, as the service holding the repository says, folds
/// into the pipeline's hover: a Dependabot update closed for a newer one otherwise sat red for as
/// long as its run stayed in the history. Where no connection can ask that service, the branch folds
/// once the default branch has built since it failed instead.
/// </para>
/// </summary>
static class BuildSelection
{
    /// <param name="verdicts">What the services holding repositories said about failed branches,
    /// by <see cref="BranchVerdicts.KeyOf(Build)"/>.</param>
    /// <param name="askable">Whether a connection can ask the service holding a build's repository
    /// about its branch. A failed branch that can be asked keeps its row until the answer comes.</param>
    public static ImmutableArray<PipelineBuilds> Select(IEnumerable<Build> builds, bool showOtherBranches, ImmutableDictionary<string, BranchVerdict> verdicts, Func<Build, bool> askable)
    {
        var runs = ByPipeline(builds, out var counts);
        var result = ImmutableArray.CreateBuilder<PipelineBuilds>(counts.Count);
        // One list for every pipeline's branches, emptied between them: most pipelines have runs
        // on one branch only and put nothing in it.
        var branches = new List<(string? Branch, Build Newest, Build? Named)>();
        var start = 0;
        foreach (var count in counts)
        {
            var pipeline = runs.AsSpan(start, count);
            start += count;
            NewestFirst(pipeline);
            var head = HeadOf(pipeline);
            if (!showOtherBranches)
            {
                result.Add(new(head, [], []));
                continue;
            }

            Branches(pipeline, head, branches);
            if (branches.Count == 0)
            {
                result.Add(new(head, [], []));
                continue;
            }

            var lanes = new List<Build>();
            var folded = ImmutableArray.CreateBuilder<FoldedBranch>();
            foreach (var (_, newest, named) in branches)
            {
                var lane = LaneOf(newest, named);
                if (FoldOf(head, lane, verdicts, askable) is { } reason)
                {
                    folded.Add(new(lane, reason));
                }
                else
                {
                    lanes.Add(lane);
                }
            }

            result.Add(new(head, MostUrgentFirst(lanes), folded.ToImmutable()));
        }

        return result.MoveToImmutable();
    }

    /// <summary>
    /// Every build beside the others of its pipeline, in one array: the pipelines in the order the
    /// first build of each came, each one's builds in the order they came, and how many each has.
    /// By the parts of the pipeline key, which would otherwise be built for every build on every
    /// projection of the rows, and in one array rather than a grouping of each pipeline, which
    /// with the ordered copy and the lookup of branches made of it was a megabyte a projection of a
    /// large account.
    /// </summary>
    static Build[] ByPipeline(IEnumerable<Build> builds, out List<int> counts)
    {
        var list = builds as IReadOnlyList<Build> ?? builds.ToList();
        counts = [];
        var ordinals = new int[list.Count];
        var pipelines = new Dictionary<(string ConnectionId, string PipelineId), int>();
        Build? previous = null;
        var ordinal = -1;
        for (var index = 0; index < list.Count; index++)
        {
            var build = list[index];
            // A poll hands over a pipeline's runs together, so most builds join the pipeline the
            // build before them did.
            if (previous is null ||
                !build.SamePipeline(previous))
            {
                var key = (build.ConnectionId, build.PipelineId);
                if (!pipelines.TryGetValue(key, out ordinal))
                {
                    ordinal = counts.Count;
                    pipelines[key] = ordinal;
                    counts.Add(0);
                }
            }

            counts[ordinal]++;
            ordinals[index] = ordinal;
            previous = build;
        }

        var next = new int[counts.Count];
        var offset = 0;
        for (var index = 0; index < next.Length; index++)
        {
            next[index] = offset;
            offset += counts[index];
        }

        var grouped = new Build[list.Count];
        for (var index = 0; index < grouped.Length; index++)
        {
            grouped[next[ordinals[index]]++] = list[index];
        }

        return grouped;
    }

    /// <summary>
    /// Sorts a pipeline's runs newest first, in place, runs of the same moment staying in the order
    /// they came. A pipeline holds a handful of runs, already newest first as most services list
    /// them, which an insertion sort passes over once; a pipeline with many is sorted as before.
    /// </summary>
    static void NewestFirst(Span<Build> runs)
    {
        if (runs.Length > 64)
        {
            runs.ToArray()
                .OrderByDescending(When)
                .ToArray()
                .CopyTo(runs);
            return;
        }

        for (var index = 1; index < runs.Length; index++)
        {
            var run = runs[index];
            var when = When(run);
            var position = index - 1;
            while (position >= 0 &&
                   When(runs[position]) < when)
            {
                runs[position + 1] = runs[position];
                position--;
            }

            runs[position + 1] = run;
        }
    }

    static DateTimeOffset When(Build build) =>
        build.Ordering ?? DateTimeOffset.MinValue;

    /// <summary>
    /// The newest run on the default branch the runs carry, or the newest run where they carry none
    /// or none of them is on it.
    /// </summary>
    static Build HeadOf(ReadOnlySpan<Build> ordered)
    {
        string? defaultBranch = null;
        foreach (var run in ordered)
        {
            if (run.DefaultBranch is not null)
            {
                defaultBranch = run.DefaultBranch;
                break;
            }
        }

        if (defaultBranch is not null)
        {
            foreach (var run in ordered)
            {
                if (run.Branch == defaultBranch)
                {
                    return run;
                }
            }
        }

        return ordered[0];
    }

    /// <summary>
    /// The branches other than the head's, in the order the newest run of each came: its newest
    /// run, and the newest of its runs to name a pull request, which <see cref="LaneOf"/> reads.
    /// </summary>
    static void Branches(ReadOnlySpan<Build> ordered, Build head, List<(string? Branch, Build Newest, Build? Named)> branches)
    {
        branches.Clear();
        foreach (var run in ordered)
        {
            if (run.Branch == head.Branch)
            {
                continue;
            }

            var index = IndexOf(branches, run.Branch);
            if (index < 0)
            {
                branches.Add((run.Branch, run, run.PullRequestNumber is null ? null : run));
                continue;
            }

            if (branches[index].Named is null &&
                run.PullRequestNumber is not null)
            {
                branches[index] = branches[index] with
                {
                    Named = run
                };
            }
        }
    }

    /// <summary>
    /// A loop rather than a lambda holding the branch, which would be made for every run of every
    /// pipeline, on the head's branch or not.
    /// </summary>
    static int IndexOf(List<(string? Branch, Build Newest, Build? Named)> branches, string? branch)
    {
        for (var index = 0; index < branches.Count; index++)
        {
            if (branches[index].Branch == branch)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The lanes, most urgent first. Sorted only where there is more than one to sort.
    /// </summary>
    static ImmutableArray<Build> MostUrgentFirst(List<Build> lanes)
    {
        if (lanes.Count < 2)
        {
            return [..lanes];
        }

        return
        [
            ..lanes
                .OrderBy(_ => _.Rank())
                .ThenByDescending(When)
                .ThenBy(_ => _, Build.ByKey)
        ];
    }

    /// <summary>
    /// Why a branch's newest run gets no row, or null where it gets one. A failed run waits for the
    /// answer where its repository can be asked, rather than folding on a guess that the answer then
    /// takes back.
    /// </summary>
    static FoldReason? FoldOf(Build head, Build lane, ImmutableDictionary<string, BranchVerdict> verdicts, Func<Build, bool> askable)
    {
        if (!lane.NeedsAttention())
        {
            return FoldReason.Settled;
        }

        if (lane.Status != BuildStatus.Failed)
        {
            return null;
        }

        var verdict = BranchVerdicts.For(verdicts, lane);
        switch (verdict?.Fate)
        {
            case BranchFate.Open:
                return null;
            case BranchFate.Merged:
                return FoldReason.Merged;
            case BranchFate.Closed:
                return FoldReason.Closed;
            case BranchFate.Deleted:
                return FoldReason.Deleted;
        }

        if ((verdict is not null || !askable(lane)) &&
            Superseded(head, lane))
        {
            return FoldReason.Superseded;
        }

        return null;
    }

    /// <summary>
    /// Whether the pipeline's default branch has built since <paramref name="lane"/> failed. Only a
    /// head on the default branch says so: where none is known, the head is just the newest run, and
    /// every other branch is older than that.
    /// </summary>
    static bool Superseded(Build head, Build lane) =>
        head.DefaultBranch is not null &&
        head.Branch == head.DefaultBranch &&
        head.Ordering > (lane.Finished ?? lane.Ordering);

    /// <summary>
    /// The branch's newest run, carrying the pull request an older run of it named where it names
    /// none itself. AppVeyor builds a pull request's branch and then the pull request, two runs of
    /// one branch of which only one knows the pull request, and the lane would lose its PR button
    /// whenever the other was the newer.
    /// </summary>
    /// <param name="named">The newest of the branch's runs to name a pull request, or null.</param>
    static Build LaneOf(Build newest, Build? named)
    {
        if (newest.PullRequestNumber is not null ||
            named is null)
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
