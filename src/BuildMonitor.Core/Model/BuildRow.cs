/// <summary>
/// One line of the builds page, already composed: a head shows these strings and never derives
/// its own, so the three of them cannot drift. The two name cells are named for their place, not
/// their content, so what each holds is decided here once rather than in every head.
/// </summary>
/// <param name="Name">The first cell, drawn bright: the project of a build or a group, and empty
/// for a build under its group, whose row above already names the project.</param>
/// <param name="NameLink">What a click on the name opens: <see cref="ChipKind.Build"/> where the
/// pipeline is named after the project, so the name is the build's too and the second cell leaves
/// it out, else <see cref="ChipKind.None"/>. Without it such a row would name its run nowhere a
/// click could reach.</param>
/// <param name="Detail">The second cell, in runs: the pipeline, linked to the run, and the branch,
/// linked to the branch where the provider gave it a page; or a group's count.</param>
/// <param name="Provider">The provider whose icon leads the second cell, and opens the project
/// when clicked, or empty for a group's own row.</param>
/// <param name="Progress">0 to 1 while a bar should be drawn, -1 when there is nothing to
/// estimate against.</param>
/// <param name="Timing">The countdown, over-run, elapsed or age text beside the bar.</param>
/// <param name="Expanded">Whether a group's members follow its row.</param>
/// <param name="Chips">The buttons, in the order they are drawn. Empty for a group's own row:
/// which of its builds they would act on is ambiguous.</param>
/// <param name="Author">Who broke a failed build, as <see cref="AuthorNames"/> calls them; empty
/// for any other row.</param>
record BuildRow(
    RowKind Kind,
    BuildStatus Status,
    string Name,
    ChipKind NameLink,
    IReadOnlyList<DetailSpan> Detail,
    string Provider,
    double Progress,
    string Timing,
    bool Selected,
    bool Expanded,
    IReadOnlyList<RowChip> Chips,
    string Author = "")
{
    /// <summary>
    /// The runs of <see cref="Detail"/> joined, for a head that measures or prints the cell whole.
    /// Derived rather than stored, so it cannot say something the runs do not.
    /// </summary>
    public string DetailText =>
        string.Concat(Detail.Select(_ => _.Text));
}
