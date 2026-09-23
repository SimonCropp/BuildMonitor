/// <summary>
/// The answers the services holding repositories gave about failed branches, keyed by what was
/// asked: the repository, the branch as its row names it, and the pull request its run was for. By
/// repository rather than by pipeline, since every workflow of a repository that failed on one pull
/// request is answered by the one question about it.
/// </summary>
static class BranchVerdicts
{
    /// <summary>
    /// The key of what <paramref name="build"/> would be asked about, or null for a build that no
    /// service could be asked about: one with no repository address or no branch.
    /// </summary>
    public static string? KeyOf(Build build)
    {
        if (build.RepoUrl is not { } repository ||
            build.Branch is not { } branch)
        {
            return null;
        }

        return KeyOf(repository, branch, build.PullRequestNumber);
    }

    public static string KeyOf(string repository, string branch, string? pullRequest) =>
        $"{repository}|{branch}|{pullRequest}";

    /// <summary>
    /// The answer that speaks for <paramref name="run"/>, or null where none was given, or where it
    /// was given before the run started. See <see cref="BranchVerdict.Covers"/>.
    /// </summary>
    public static BranchVerdict? For(ImmutableDictionary<string, BranchVerdict> verdicts, Build run)
    {
        if (verdicts.IsEmpty ||
            KeyOf(run) is not { } key ||
            !verdicts.TryGetValue(key, out var verdict) ||
            !verdict.Covers(run))
        {
            return null;
        }

        return verdict;
    }

    /// <summary>
    /// The other branches whose newest run failed, whether they have a row or were folded: the ones
    /// a service could be asked about.
    /// </summary>
    public static IEnumerable<Build> Failed(ImmutableArray<PipelineBuilds> pipelines)
    {
        foreach (var pipeline in pipelines)
        {
            foreach (var lane in pipeline.Lanes)
            {
                if (lane.Status == BuildStatus.Failed)
                {
                    yield return lane;
                }
            }

            foreach (var folded in pipeline.Folded)
            {
                if (folded.Build.Status == BuildStatus.Failed)
                {
                    yield return folded.Build;
                }
            }
        }
    }
}
