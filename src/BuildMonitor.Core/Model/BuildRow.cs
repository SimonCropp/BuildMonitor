/// <summary>
/// One line of the builds page, already composed: a head shows these strings and never derives
/// its own, so the three of them cannot drift.
/// </summary>
/// <param name="Progress">0 to 1 while a bar should be drawn, -1 when there is nothing to
/// estimate against.</param>
/// <param name="Timing">The countdown, over-run, elapsed or age text beside the bar.</param>
record BuildRow(
    RowKind Kind,
    BuildStatus Status,
    string Pipeline,
    string RepoBranch,
    string RunNumber,
    string StatusText,
    double Progress,
    string Timing,
    bool Selected,
    bool Folded,
    LinkChip? Build,
    LinkChip? Branch,
    LinkChip? PullRequest,
    bool CanRetry,
    bool CanCancel,
    string Tooltip,
    string Group);
