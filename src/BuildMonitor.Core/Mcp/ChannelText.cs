/// <summary>
/// What each channel event says. Written for the assistant that reads it in an idle session: what
/// happened, to which build, and the key the tools take, so it can act without listing builds to
/// find out which one was meant.
/// </summary>
static class ChannelText
{
    // What each event is, in its tag's event attribute.
    public const string BuildFailed = "build_failed";
    public const string BuildFinished = "build_finished";
    public const string PushFinished = "push_finished";
    public const string PushFailed = "push_failed";
    public const string WatchFailedKind = "watch_failed";
    public const string WatchTimedOut = "watch_timed_out";

    /// <summary>
    /// Every kind of event, which the instructions the server sends must each explain.
    /// </summary>
    public static IReadOnlyList<string> Kinds { get; } = [BuildFailed, BuildFinished, PushFinished, PushFailed, WatchFailedKind, WatchTimedOut];

    public static ChannelEvent Failed(BuildDto build) =>
        new(
            $"{Name(build)} failed. Key: {build.Key}. get_build_log with that key says why.",
            Meta(BuildFailed, build, "failed"));

    /// <summary>
    /// What watch_build answers at once, before the event: which run it is watching.
    /// </summary>
    public static string Watching(BuildDto build, bool nextRun, WaitScope scope)
    {
        var what = $"{build.Pipeline} {build.Run}";
        if (nextRun &&
            !IsActive(build.Status))
        {
            what = $"the run after {what}";
        }

        if (scope != WaitScope.Run)
        {
            what += " and the rest of its push";
        }

        return $"Watching {what}. An event arrives in this session when it finishes, as a channel tag.";
    }

    public static ChannelEvent Finished(BuildWaitDto wait, TimeSpan timeout)
    {
        var build = wait.Build;
        if (wait.PushRuns is { } runs)
        {
            return Pushed(build, runs, wait.TimedOut, timeout);
        }

        if (wait.TimedOut)
        {
            return new(
                $"Stopped watching {Name(build)}: still {build.Status.ToLowerInvariant()} after {Hours(timeout)}. Key: {build.Key}.",
                Meta(WatchTimedOut, build, build.Status.ToLowerInvariant()));
        }

        var outcome = Outcome(build.Status, build.StatusText);
        return new(
            $"{Name(build)} {outcome}. Key: {build.Key}.",
            Meta(BuildFinished, build, Word(build.Status)));
    }

    /// <summary>
    /// A push's runs counted by how they ended, and the failures named, since a push with a failure
    /// in it is answered by which one.
    /// </summary>
    static ChannelEvent Pushed(BuildDto build, List<BuildRowDto> runs, bool timedOut, TimeSpan timeout)
    {
        var push = $"the push of {Short(build.Commit)} to {build.Repo}";
        if (build.Branch is { } branch)
        {
            push += $" on {branch}";
        }

        var failed = runs.Where(_ => _.Status == nameof(BuildStatus.Failed)).ToList();
        var going = runs.Count(_ => IsActive(_.Status));
        var named = string.Join(", ", failed.Select(_ => $"{_.Pipeline} {_.Run}"));
        if (timedOut)
        {
            return new(
                $"Stopped watching {push}: {runs.Count - going} of {runs.Count} runs finished after {Hours(timeout)}. Key: {build.Key}.",
                Meta(WatchTimedOut, build, "running"));
        }

        if (going > 0)
        {
            return new(
                $"A run of {push} failed: {named}. {going} still going. Key: {build.Key}.",
                Meta(PushFailed, build, "failed"));
        }

        var passed = runs.Count(_ => _.Status == nameof(BuildStatus.Succeeded));
        var counts = $"{passed} passed, {failed.Count} failed";
        var other = runs.Count - passed - failed.Count;
        if (other > 0)
        {
            counts += $", {other} neither";
        }

        if (failed.Count > 0)
        {
            counts += $" ({named})";
        }

        return new(
            $"{char.ToUpperInvariant(push[0])}{push[1..]} finished: {counts}. Key: {build.Key}.",
            Meta(PushFinished, build, failed.Count > 0 ? "failed" : "passed"));
    }

    public static ChannelEvent WatchFailed(string key, string message) =>
        new(
            $"Could not watch {key}: {message}",
            new Dictionary<string, string>
            {
                ["event"] = WatchFailedKind,
                ["key"] = key
            });

    static string Name(BuildDto build)
    {
        var name = $"{build.Pipeline} {build.Run}";
        if (build.Branch is { } branch)
        {
            name += $" on {branch}";
        }

        return $"{name} in {build.Repo}";
    }

    static string Outcome(string status, string? statusText) =>
        status switch
        {
            nameof(BuildStatus.Succeeded) => "passed",
            nameof(BuildStatus.Failed) => "failed",
            nameof(BuildStatus.Cancelled) => "was cancelled",
            _ => $"ended ({statusText ?? "unknown"})"
        };

    static string Word(string status) =>
        status switch
        {
            nameof(BuildStatus.Succeeded) => "passed",
            nameof(BuildStatus.Failed) => "failed",
            nameof(BuildStatus.Cancelled) => "cancelled",
            _ => "unknown"
        };

    static bool IsActive(string status) =>
        status is nameof(BuildStatus.Queued) or nameof(BuildStatus.Running);

    static string Hours(TimeSpan timeout) =>
        $"{timeout.TotalHours:0} hours";

    static string? Short(string? commit)
    {
        if (commit is { Length: > 7 })
        {
            return commit[..7];
        }

        return commit;
    }

    static Dictionary<string, string> Meta(string kind, BuildDto build, string status) =>
        new()
        {
            ["event"] = kind,
            ["key"] = build.Key,
            ["run"] = build.Run,
            ["status"] = status
        };
}
