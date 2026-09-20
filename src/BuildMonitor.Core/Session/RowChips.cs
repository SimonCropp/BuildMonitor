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
    public static readonly string[] Icons = ["pull-request", "retry", "cancel", "log", "folder", "triage"];

    /// <summary>
    /// In <see cref="ChipKind"/> order, which is the order a head draws them in.
    /// </summary>
    public static IReadOnlyList<RowChip> Of(Build build, ProviderDescriptor descriptor, ImmutableDictionary<string, string> localRepos)
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

        if (build.LogCopyable())
        {
            chips.Add(new(ChipKind.CopyLog, "Log", "Copy failure log", "log"));
        }

        // Through the same lookup the click goes through, so a row cannot show a button that then
        // opens nothing. The tooltip carries the directory, which is the one thing about this
        // button a row cannot show and the reader cannot guess.
        var directory = LocalRepos.Find(localRepos, build);
        if (directory is not null)
        {
            chips.Add(new(ChipKind.OpenDirectory, "Open dir", $"Open folder: {directory}", "folder"));
        }

        // Both halves, through the same two lookups the click goes through: a log is only worth
        // fetching for a failure, and without a checkout there is nowhere for the prompt to send
        // an assistant once it has the files.
        if (build.LogCopyable() &&
            directory is not null)
        {
            chips.Add(new(ChipKind.Triage, "Triage", $"Download artifacts and log, and copy a prompt\n{directory}", "triage"));
        }

        return chips;
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
            ChipKind.Triage => CommandKind.Triage,
            _ => CommandKind.None
        };
}
