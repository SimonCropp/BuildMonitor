/// <summary>
/// The one rule about what a <see cref="Deferral"/> hides and when it ends. Only a failed build
/// is hidden: a deferred pipeline that is running again is worth watching, and one that passed has
/// nothing left to put off.
/// </summary>
static class Deferrals
{
    public static readonly ImmutableArray<int> Days = [1, 3, 7];

    public static bool Hides(ImmutableArray<Deferral> deferrals, Build build)
    {
        if (build.Status != BuildStatus.Failed)
        {
            return false;
        }

        foreach (var deferral in deferrals)
        {
            if (build.HasKey(deferral.Key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The deferrals still standing: not yet due, and not of a pipeline whose latest run on the
    /// branch has since passed. A fix ends one early, as a deferral kept past it would hide the
    /// next break of that pipeline, which is news rather than the failure that was put off. The
    /// same array when nothing ended, so the settings, and every projection cached against them,
    /// stay the same instance.
    /// </summary>
    public static ImmutableArray<Deferral> Standing(ImmutableArray<Deferral> deferrals, ImmutableArray<Build> builds, DateTimeOffset now)
    {
        if (deferrals.Length == 0)
        {
            return deferrals;
        }

        var kept = deferrals.RemoveAll(_ => _.Until <= now || Fixed(_, builds));
        if (kept.Length == deferrals.Length)
        {
            return deferrals;
        }

        return kept;
    }

    static bool Fixed(Deferral deferral, ImmutableArray<Build> builds)
    {
        Build? latest = null;
        foreach (var build in builds)
        {
            if (!build.HasKey(deferral.Key))
            {
                continue;
            }

            if (latest is null ||
                (build.Ordering ?? DateTimeOffset.MinValue) > (latest.Ordering ?? DateTimeOffset.MinValue))
            {
                latest = build;
            }
        }

        return latest?.Status == BuildStatus.Succeeded;
    }

    /// <summary>
    /// What the menu item for <paramref name="days"/> says.
    /// </summary>
    public static string Label(int days) =>
        $"Defer {Span(days)}";

    public static string Span(int days) =>
        days == 1 ? "1 day" : $"{days} days";

    /// <summary>
    /// How long is left, rounded up, as the filters page says it: a deferral due in an hour is not
    /// "0 days left".
    /// </summary>
    public static string Remaining(Deferral deferral, DateTimeOffset now)
    {
        var left = deferral.Until - now;
        if (left <= TimeSpan.Zero)
        {
            return "due";
        }

        if (left < TimeSpan.FromDays(1))
        {
            var hours = (int) Math.Ceiling(left.TotalHours);
            return hours == 1 ? "1 hour left" : $"{hours} hours left";
        }

        var days = (int) Math.Ceiling(left.TotalDays);
        return days == 1 ? "1 day left" : $"{days} days left";
    }
}
