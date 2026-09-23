/// <summary>
/// What the service holding a repository said about a failed branch, and when it said it.
/// </summary>
record BranchVerdict(BranchFate Fate, DateTimeOffset At)
{
    /// <summary>
    /// Whether this speaks for <paramref name="run"/>: the run started before it was said. A branch
    /// recreated after it was found deleted, or a pull request reopened and pushed to, starts a run
    /// the answer knew nothing about, and folding that on the old answer would hide a new failure.
    /// </summary>
    public bool Covers(Build run) =>
        run.Ordering is not { } started ||
        started <= At;
}
