/// <summary>
/// Which branch a pipeline's own runs are on, for a service whose setting for it cannot be taken on
/// trust. AppVeyor keeps the default branch a project had when it was added, so a repository that
/// moved from master to main is still master there, and its pipeline would be headed by a branch
/// nothing builds any more. What the builds themselves show is the better witness: a pull request is
/// opened against the default branch unless someone chose otherwise, and main keeps being pushed to.
/// </summary>
static class DefaultBranches
{
    /// <summary>
    /// The first candidate the window bears out, as the target of a pull request build or the
    /// branch of any other build; failing that, the branch most of the pull request builds target,
    /// the newest of those that tie; and with no pull request builds to go by, the first candidate.
    /// <para>
    /// A candidate the window bears out is kept even where more of its pull requests target another
    /// branch, so one backport window does not hand the pipeline to a release branch.
    /// </para>
    /// </summary>
    /// <param name="candidates">What the branch was said or last found to be, most trusted first.
    /// Nulls are skipped.</param>
    /// <param name="targets">The branches the window's pull request builds target, newest first.</param>
    /// <param name="built">The branches the window's other builds ran on.</param>
    public static string? Choose(IEnumerable<string?> candidates, IReadOnlyList<string> targets, IReadOnlyCollection<string> built)
    {
        var known = candidates.OfType<string>().ToList();
        foreach (var candidate in known)
        {
            if (targets.Contains(candidate) ||
                built.Contains(candidate))
            {
                return candidate;
            }
        }

        if (targets.Count == 0)
        {
            return known.FirstOrDefault();
        }

        return targets
            .GroupBy(_ => _)
            .OrderByDescending(_ => _.Count())
            .First()
            .Key;
    }
}
