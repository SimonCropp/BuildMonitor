/// <summary>
/// The glyph name for the logo of the service that ran a build. Every one of them carries a badge
/// in its corner, because one of them has to: GitHub's logo is also a source host's, and a row
/// draws both at once. The same treatment on all ten, rather than on the one that needs it, so
/// that a column of rows reads as one thing.
/// <para>
/// A badge is read as a state, so it has to be one: the play mark on a row that broke said the
/// run was going, which is the one thing it was not. Each logo has a second glyph, badged with a
/// cross, for that.
/// </para>
/// </summary>
static class ProviderMarks
{
    public static string Of(string providerId, BuildStatus status)
    {
        if (status == BuildStatus.Failed)
        {
            return $"provider-{providerId}-failed";
        }

        return $"provider-{providerId}";
    }

    /// <summary>
    /// Every name <see cref="Of"/> can return. A head hands its pictures over once, before the
    /// first frame, rather than as a row asks for one.
    /// </summary>
    public static IEnumerable<string> All =>
        ProviderDescriptors.All
            .SelectMany(_ => Names(_.Id));

    static IEnumerable<string> Names(string providerId)
    {
        yield return $"provider-{providerId}";
        yield return $"provider-{providerId}-failed";
    }
}
