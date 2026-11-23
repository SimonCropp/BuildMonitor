/// <summary>
/// The buttons a build's row carries and what each does. One list for the chips a head draws, the
/// drop down that holds those a narrow window has no room for, and the command behind a click on
/// either, so a chip hidden in the drop down cannot offer something its visible twin would not.
/// The run and the branch are not chips: their names in the row's text are the links, and
/// <see cref="Command"/> maps a click on those too.
/// </summary>
static class RowChips
{
    /// <summary>
    /// Every picture a chip can carry. A head that uploads its textures before the first frame
    /// needs the whole set up front, and this is the file that decides it: a name added to a chip
    /// below and not here draws an empty button, which is what happened when Cancel stopped being
    /// a word.
    /// </summary>
    public static readonly string[] Icons = ["pull-request", "retry", "cancel", "log", "folder", "triage", ..Enumerable.Range(0, BusyFrames).Select(_ => $"busy-{_}"), "run-next", "clone"];

    /// <summary>
    /// The pictures of a triage chip still collecting: a clock whose hand steps round once a second.
    /// A still hourglass was taken for a copy already made, and pasted before the prompt was there.
    /// </summary>
    public const int BusyFrames = 12;

    /// <summary>
    /// The order a row's chips are drawn in, left to right, and so what a drop down's "every chip
    /// from this one on" counts by. A list of its own rather than the order of the
    /// <see cref="ChipKind"/> values, whose numbers are the wire format a committed native binary
    /// reports, so a chip added later could only go at the end. Triage comes before the log and
    /// the checkout: it fetches the log and opens the prompt in the checkout, so it is the one of
    /// the three a failure is clicked for, and a narrow row puts the last of its chips in the drop
    /// down, where it was the triage that went. The clone sits where the checkout does: a row
    /// carries the one or the other, never both.
    /// </summary>
    static readonly ChipKind[] order = [ChipKind.PullRequest, ChipKind.Retry, ChipKind.Cancel, ChipKind.Triage, ChipKind.CopyLog, ChipKind.OpenDirectory, ChipKind.Clone, ChipKind.RunNext];

    /// <summary>
    /// The chips from <paramref name="from"/> on, in the order they are drawn. By kind rather than
    /// by position, so a chip the build lost since the row was drawn, such as Cancel on a build
    /// that has finished, neither shows nor shifts which chips follow it.
    /// </summary>
    public static IEnumerable<RowChip> From(IEnumerable<RowChip> chips, ChipKind from) =>
        chips.Where(_ => Array.IndexOf(order, _.Kind) >= Array.IndexOf(order, from));

    /// <summary>
    /// In <see cref="order"/>, which is the order a head draws them in.
    /// </summary>
    /// <param name="triaging">Whether a triage of this run is still collecting, as
    /// <see cref="MonitorSession.IsTriaging"/> says.</param>
    /// <param name="tick">The clock in whole seconds, which picks the busy chip's frame. The clock
    /// rather than the time since the click: <see cref="ScreenCache"/> rebuilds on the clock's whole
    /// seconds, so a frame taken from the same second steps exactly once a rebuild, where one counted
    /// from the click skipped or repeated a frame whenever a rebuild landed either side of a second.
    /// The drop down leaves it out, since it draws only the labels.</param>
    /// <param name="assistant">The AI app a triage opens, as <see cref="Assistants.Resolve"/> picks
    /// it, or null where it only copies. Only the triage chip's hover says which, so the drop down
    /// leaves it out too.</param>
    /// <param name="cloneable">Whether GitHub Desktop is offered a clone of the repository, as
    /// <see cref="GitHubDesktop.CloneLink(SessionState, Build)"/> decides it. A flag rather than
    /// the inputs to that rule, so the chip cannot be decided by a rule of its own.</param>
    public static IReadOnlyList<RowChip> Of(Build build, ProviderDescriptor descriptor, ImmutableDictionary<string, string> localRepos, bool triaging, long tick = 0, Assistant? assistant = null, bool cloneable = false)
    {
        List<RowChip> chips = [];
        // The number is the one thing about a pull request worth a row's width: which one it is.
        // The rest is the icon, and the whole of it is in the tooltip.
        if (build.PullRequestUrl is not null)
        {
            var number = build.PullRequestNumber;
            chips.Add(new(
                ChipKind.PullRequest,
                number is null ? "PR" : $"PR {number}",
                number is null ? "Open PR" : $"Open PR: {number}",
                "pull-request",
                number ?? ""));
        }

        // Retry and Cancel name the service: they are the two buttons that change what is running
        // on someone's CI, and a row does not otherwise say which one it would reach.
        if (build.Retryable())
        {
            chips.Add(new(ChipKind.Retry, "Retry", $"Run again on {descriptor.Name}", "retry"));
        }

        if (build.CanCancel)
        {
            chips.Add(new(ChipKind.Cancel, "Cancel", $"Cancel this run on {descriptor.Name}", "cancel"));
        }

        // Through the same lookup the click goes through, so a row cannot show a button that then
        // opens nothing.
        var directory = LocalRepos.Find(localRepos, build);

        // Both halves, through the same two lookups the click goes through: a log is only worth
        // fetching for a failure, and without a checkout there is nowhere for the prompt to send
        // an assistant once it has the files.
        if (build.LogCopyable() &&
            directory is not null)
        {
            // The same kind while it collects, so it stays in its place on the row, but a
            // different picture, and one that moves: the prompt is only copied at the end, and a
            // chip that looked the same throughout had people pasting whatever they had copied
            // before clicking it. A picture rather than a count of seconds, which would widen the
            // chip past the column sized for it and send it into the drop down. A click on it does
            // nothing, which the applier decides, not this.
            chips.Add(triaging
                ? new(ChipKind.Triage, "Triaging", TriagingTooltip(assistant), $"busy-{tick % BusyFrames}")
                : new(ChipKind.Triage, "Triage", $"{TriageTooltip(assistant)}\n{directory}", "triage"));
        }

        if (build.LogCopyable())
        {
            chips.Add(new(ChipKind.CopyLog, "Log", "Copy failure log", "log"));
        }

        // The tooltip carries the directory, which is the one thing about this button a row cannot
        // show and the reader cannot guess.
        if (directory is not null)
        {
            chips.Add(new(ChipKind.OpenDirectory, "Open dir", $"Open folder: {directory}", "folder"));
        }

        // Named for the app it opens, for the reason the triage chip names its: the click brings
        // GitHub Desktop up over the window. The hover carries the address being cloned, as the
        // folder's carries the directory: the row itself shows only the repository's name.
        if (cloneable &&
            directory is null)
        {
            chips.Add(new(ChipKind.Clone, "Clone", $"Clone in GitHub Desktop: {build.RepoUrl}", "clone"));
        }

        // Last, as in order. It names the service for the reason Retry and Cancel do, being the
        // third button that changes what someone's CI is about to run, and it says what happens
        // rather than what is set, since a position in a queue is only worth knowing as the order
        // things will run in.
        if (build.CanRunNext(descriptor))
        {
            chips.Add(new(ChipKind.RunNext, "Run next", $"Run this next on {descriptor.Name}, ahead of the rest of the queue", "run-next"));
        }

        return chips;
    }

    /// <summary>
    /// Names the app before the click, since the click is what brings it forward, and an app that
    /// comes up unannounced over the window reads as something else having opened it.
    /// </summary>
    static string TriageTooltip(Assistant? assistant)
    {
        if (assistant is null)
        {
            return "Download artifacts and log, and copy a prompt";
        }

        return $"Download artifacts and log, and open a prompt in {assistant.Name}";
    }

    static string TriagingTooltip(Assistant? assistant)
    {
        if (assistant is null)
        {
            return "Collecting artifacts and log. A notification says when the prompt is on the clipboard.";
        }

        return $"Collecting artifacts and log. {assistant.Name} opens with the prompt when they are in.";
    }

    public static CommandKind Command(ChipKind kind) =>
        kind switch
        {
            ChipKind.Build => CommandKind.OpenBuild,
            ChipKind.Branch => CommandKind.OpenBranch,
            ChipKind.PullRequest => CommandKind.OpenPullRequest,
            ChipKind.Retry => CommandKind.Retry,
            ChipKind.Cancel => CommandKind.Cancel,
            ChipKind.CopyLog => CommandKind.CopyLog,
            ChipKind.Pipeline => CommandKind.OpenPipeline,
            ChipKind.Repo => CommandKind.OpenRepo,
            ChipKind.OpenDirectory => CommandKind.OpenRepoDirectory,
            ChipKind.Clone => CommandKind.CloneRepo,
            ChipKind.Triage => CommandKind.Triage,
            ChipKind.RunNext => CommandKind.RunNext,
            _ => CommandKind.None
        };
}
