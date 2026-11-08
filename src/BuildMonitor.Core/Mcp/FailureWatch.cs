/// <summary>
/// Which failing builds are news since the last look.
/// <para>
/// A failure is news once: one look to the next, a red pipeline stays red, and said again on every
/// look it would bury the session. It is known by its key, run and finish, so a retry that fails
/// again under the same run number is news too. What has been seen is kept for the life of the
/// watch, not just the last look's, since a tray restarting answers with nothing until it has
/// polled, and every failure it then lists again would otherwise read as new.
/// </para>
/// <para>
/// A failure is news only from a connection that had been polled by the last look. The tray this
/// server started itself, and a tray just restarted, has polled nothing, and its first poll lists
/// every failure the account has, which are not news to anyone: told them, a session started on a
/// red account heard of every one of them a minute in.
/// </para>
/// </summary>
sealed class FailureWatch
{
    HashSet<(string Key, string Run, DateTimeOffset? Finished)> seen = [];
    HashSet<string> polled = [];

    /// <param name="failing">The failing builds now, as list_failing has them.</param>
    /// <param name="connections">Every connection, of which those polled at least once are kept.</param>
    public List<BuildDto> Next(List<BuildDto> failing, List<ConnectionDto> connections)
    {
        var news = failing
            .Where(_ => polled.Contains(_.Connection) &&
                        !seen.Contains(Identity(_)))
            .ToList();
        seen.UnionWith(failing.Select(Identity));
        polled = connections
            .Where(_ => _.LastPolled is not null)
            .Select(_ => _.Name)
            .ToHashSet();
        return news;
    }

    static (string Key, string Run, DateTimeOffset? Finished) Identity(BuildDto build) =>
        (build.Key, build.Run, build.Finished);
}
