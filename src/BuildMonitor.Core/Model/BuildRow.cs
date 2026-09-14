/// <summary>
/// One line of the builds page, already composed: a head shows these strings and never derives
/// its own, so the three of them cannot drift. The two name cells are named for their place, not
/// their content, so what each holds is decided here once rather than in every head.
/// </summary>
/// <param name="Name">The first cell, drawn bright: the project of a build or a group, and empty
/// for a build under its group, whose row above already names the project.</param>
/// <param name="Detail">The second cell, drawn dimmed: the pipeline and branch, or a group's
/// count.</param>
/// <param name="Provider">The provider whose icon leads the second cell, or empty when every
/// connection is one provider and an icon would tell rows apart by nothing.</param>
/// <param name="Progress">0 to 1 while a bar should be drawn, -1 when there is nothing to
/// estimate against.</param>
/// <param name="Timing">The countdown, over-run, elapsed or age text beside the bar.</param>
/// <param name="Expanded">Whether a group's members follow its row.</param>
record BuildRow(
    RowKind Kind,
    BuildStatus Status,
    string Name,
    string Detail,
    string Provider,
    string RunNumber,
    string StatusText,
    double Progress,
    string Timing,
    bool Selected,
    bool Expanded,
    LinkChip? Build,
    LinkChip? Branch,
    LinkChip? PullRequest,
    bool CanRetry,
    bool CanCancel);
