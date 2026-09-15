/// <summary>
/// The buttons a build's row carries and what each does. One list for the chips a head draws, the
/// drop down that holds those a narrow window has no room for, and the command behind a click on
/// either, so a chip hidden in the drop down cannot offer something its visible twin would not.
/// </summary>
static class RowChips
{
    /// <summary>
    /// In <see cref="ChipKind"/> order, which is the order a head draws them in.
    /// </summary>
    public static IReadOnlyList<RowChip> Of(Build build)
    {
        List<RowChip> chips = [new(ChipKind.Build, "Build")];
        if (build.BranchUrl is not null)
        {
            chips.Add(new(ChipKind.Branch, "Branch"));
        }

        if (build.PullRequestUrl is not null)
        {
            chips.Add(new(ChipKind.PullRequest, build.PullRequestNumber is null ? "PR" : $"PR {build.PullRequestNumber}"));
        }

        if (build.Retryable())
        {
            chips.Add(new(ChipKind.Retry, "Retry"));
        }

        if (build.CanCancel)
        {
            chips.Add(new(ChipKind.Cancel, "Cancel"));
        }

        if (build.LogCopyable())
        {
            chips.Add(new(ChipKind.CopyLog, "Copy log"));
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
            ChipKind.Project => CommandKind.OpenProject,
            _ => CommandKind.None
        };
}
