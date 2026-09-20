/// <summary>
/// The glyph name for the logo of the service that ran a build. Every one of them carries a badge
/// in its corner, because one of them has to: GitHub's logo is also a source host's, and a row
/// draws both at once. The same treatment on all ten, rather than on the one that needs it, so
/// that a column of rows reads as one thing.
/// <para>
/// The badge says what the mark opens, because it is read that way: the play on a row that broke
/// said the run was going, which is the one thing it was not, and the same play on the mark that
/// opens a pipeline's own page said "run" of a list of past runs.
/// </para>
/// </summary>
static class ProviderMarks
{
    /// <summary>
    /// The mark for a run: a play, or a cross where it broke.
    /// </summary>
    public static string Run(string providerId, BuildStatus status)
    {
        if (status == BuildStatus.Failed)
        {
            return $"provider-{providerId}-failed";
        }

        return $"provider-{providerId}-run";
    }

    /// <summary>
    /// The mark for the pipeline's own page on the service, which lists what it has run: a clock,
    /// in grey, so that a coloured badge keeps meaning this run and a grey one the pipeline.
    /// </summary>
    public static string Pipeline(string providerId) =>
        $"provider-{providerId}-history";

    /// <summary>
    /// Every name the two of them can return. A head hands its pictures over once, before the
    /// first frame, rather than as a row asks for one.
    /// </summary>
    public static IEnumerable<string> All =>
        ProviderDescriptors.All
            .SelectMany(_ => Names(_.Id));

    static IEnumerable<string> Names(string providerId)
    {
        yield return Run(providerId, BuildStatus.Running);
        yield return Run(providerId, BuildStatus.Failed);
        yield return Pipeline(providerId);
    }
}
