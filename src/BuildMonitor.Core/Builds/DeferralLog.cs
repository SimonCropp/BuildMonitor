/// <summary>
/// Writes each deferral to the log as it is made and as it ends, with why it ended. The settings
/// say only what stands now, so a deferral of seven days gone after two could have fallen due, been
/// ended by a pass, been taken off by hand or been lost from the file, and nothing told those
/// apart.
/// </summary>
static class DeferralLog
{
    /// <summary>
    /// What the settings file held when the tray started, so one missing after a restart shows as
    /// never saved rather than as ended.
    /// </summary>
    public static void Loaded(ImmutableArray<Deferral> deferrals)
    {
        foreach (var deferral in deferrals)
        {
            Log.Information(
                "Deferral of the {Deferral} ({Key}) stands until {Until:yyyy-MM-dd HH:mm:ss zzz}",
                deferral.Name,
                deferral.Key,
                deferral.Until.ToLocalTime());
        }
    }

    /// <summary>
    /// Every deferral in <paramref name="after"/> that was not in <paramref name="before"/> as
    /// made, and every one the other way round as ended, with <see cref="Reason"/>.
    /// </summary>
    public static void Changed(ImmutableArray<Deferral> before, ImmutableArray<Deferral> after, ImmutableArray<Build> builds, DateTimeOffset now)
    {
        if (before == after)
        {
            return;
        }

        foreach (var deferral in after)
        {
            if (!before.Contains(deferral))
            {
                Log.Information(
                    "Deferred the {Deferral} ({Key}) until {Until:yyyy-MM-dd HH:mm:ss zzz}",
                    deferral.Name,
                    deferral.Key,
                    deferral.Until.ToLocalTime());
            }
        }

        foreach (var deferral in before)
        {
            if (!after.Contains(deferral))
            {
                Log.Information(
                    "The deferral of the {Deferral} ({Key}) until {Until:yyyy-MM-dd HH:mm:ss zzz} ended: {Reason}",
                    deferral.Name,
                    deferral.Key,
                    deferral.Until.ToLocalTime(),
                    Reason(deferral, after, builds, now));
            }
        }
    }

    /// <summary>
    /// Why <paramref name="deferral"/> is not among <paramref name="after"/>. What is left once the
    /// tray's own reasons are ruled out is the user's doing: the Undo beside the status line, or the
    /// filters page.
    /// </summary>
    public static string Reason(Deferral deferral, ImmutableArray<Deferral> after, ImmutableArray<Build> builds, DateTimeOffset now)
    {
        if (after.Any(_ => _.Key == deferral.Key))
        {
            return "deferred again";
        }

        if (deferral.Until <= now)
        {
            return "it fell due";
        }

        if (Deferrals.Passed(deferral, builds) is { } passed)
        {
            return $"run {passed.RunNumber} passed";
        }

        return "taken off by Undo or the filters page";
    }
}
