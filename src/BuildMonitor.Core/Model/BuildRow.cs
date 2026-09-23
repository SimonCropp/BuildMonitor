/// <summary>
/// One line of the builds page, already composed: a head shows these strings and never derives
/// its own, so the three of them cannot drift. The two name cells are named for their place, not
/// their content, so what each holds is decided here once rather than in every head.
/// </summary>
/// <param name="Name">The first cell, drawn bright: the project of a build or a group, and empty
/// for a build under its group, whose row above already names the project.</param>
/// <param name="NameLink">What a click on the name, and on the mark before it, opens:
/// <see cref="ChipKind.Build"/> on a row that broke or is still going, which leads with its run,
/// and <see cref="ChipKind.Repo"/> on a settled one, which leads with its project. The cell names
/// the repository either way; what it opens is decided here, so no head reads the status and
/// guesses.</param>
/// <param name="NameIcon">The mark drawn before the name: the service hosting the source, or, on
/// a row that leads with its run, the service that ran it. Empty where there is no mark for
/// either. It is part of the same target as the name, and never the same picture as
/// <paramref name="DetailIcon"/>, which is the other of the two: a row shows the repository beside
/// the pipeline, so the two marks cannot be one picture and still say which is which.</param>
/// <param name="StatusLink">What a click on the status square opens: <see cref="ChipKind.Build"/>
/// on a build's row and <see cref="ChipKind.None"/> on a group's, which stands for several runs and
/// so has no one run to open. The square is the one cell every build row has, and the only part of
/// a row whose pipeline is named after its project that can reach the run. Decided here rather than
/// by each head testing the row's kind, which is how the row's links drifted apart in the first
/// place.</param>
/// <param name="Detail">The second cell, in runs: the pipeline, linked to whichever of the run
/// and its own page on the service the first cell did not take, and the branch behind its mark,
/// linked to the branch where the provider gave it a page; or a group's count.</param>
/// <param name="DetailIcon">The mark leading the second cell, the other of the row's two, or
/// empty for a group's own row.</param>
/// <param name="DetailIconLink">What a click on <paramref name="DetailIcon"/> opens:
/// <see cref="ChipKind.Pipeline"/> where it is the service that ran the build,
/// <see cref="ChipKind.Repo"/> where it is the host of the source.</param>
/// <param name="Provider">The provider's id. Only the text renderer uses it, which cannot draw a
/// logo and gives the id a column of its own instead; every other head draws the two marks.</param>
/// <param name="Progress">0 to 1 while a bar should be drawn, -1 when there is nothing to
/// estimate against.</param>
/// <param name="Timing">The countdown, over-run, elapsed or age text beside the bar.</param>
/// <param name="Expanded">Whether a group's members follow its row.</param>
/// <param name="Chips">The buttons, in the order they are drawn. Empty for a group's own row:
/// which of its builds they would act on is ambiguous.</param>
/// <param name="Author">Who broke a failed build, as <see cref="AuthorNames"/> calls them; empty
/// for any other row.</param>
/// <param name="Tooltips">What each part of the row says on hover, by the part a head is drawing.
/// A row now has five things a click can open, and without this each head would have to name them
/// itself. <see cref="RowPart.Row"/> is what the rest of the row says.</param>
record BuildRow(
    RowKind Kind,
    BuildStatus Status,
    string Name,
    ChipKind NameLink,
    string NameIcon,
    ChipKind StatusLink,
    IReadOnlyList<DetailSpan> Detail,
    string DetailIcon,
    ChipKind DetailIconLink,
    string Provider,
    double Progress,
    string Timing,
    bool Selected,
    bool Expanded,
    IReadOnlyList<RowChip> Chips,
    IReadOnlyList<RowTooltip> Tooltips,
    string Author = "")
{
    /// <summary>
    /// The text of the runs of <see cref="Detail"/> joined, without their icons, for a head that
    /// measures or prints the cell whole. Derived rather than stored, so it cannot say something
    /// the runs do not.
    /// </summary>
    public string DetailText =>
        string.Concat(Detail.Select(_ => _.Text));

    /// <summary>
    /// What <paramref name="part"/> says on hover, falling back to the row's own text so a head can
    /// ask for any part without first checking whether this row has one.
    /// </summary>
    public string Tooltip(RowPart part)
    {
        var fallback = "";
        foreach (var tooltip in Tooltips)
        {
            if (tooltip.Part == part)
            {
                return tooltip.Text;
            }

            if (tooltip.Part == RowPart.Row)
            {
                fallback = tooltip.Text;
            }
        }

        return fallback;
    }
}
