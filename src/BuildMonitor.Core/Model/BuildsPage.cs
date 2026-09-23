record BuildsPage(
    string Header,
    IReadOnlyList<BuildRow> Rows,
    int ScrollTop,
    // All rows, not the visible slice, so a scrollbar knows its travel.
    int TotalRows,
    // Index into Rows of the selected one, or -1 when it is scrolled out of view.
    int SelectedRow,
    int FailingCount,
    int RunningCount,
    // Every distinct first column name across all rows, not only the visible slice, so a head sizes
    // the column once for the whole list and it does not shift while scrolling. Group names are
    // apart because a head draws them bold and behind an arrow.
    IReadOnlyList<string> Names,
    IReadOnlyList<string> GroupNames,
    // Every distinct second column across all rows, for the same reason. The chips give way to the
    // width these want before the pipeline and branch are cut short. Text only: the branch's mark
    // is a picture, so a head adds its width to these once any row draws one.
    IReadOnlyList<string> Details,
    // No rows yet because a connection has not finished its first poll, so a head draws a spinner
    // rather than an empty page.
    bool Loading,
    // The filter box's text, which a head shows unless someone is typing in it.
    string Search,
    // What the filter box says on hover. The box has no label, so without it nothing says what it
    // matches against.
    string SearchTooltip,
    // What the body says when it has no rows, composed once so every head says the same: loading,
    // nothing matching the filter, or nothing yet. Empty while there are rows.
    string Empty,
    // Every distinct author shown across all failed builds, to size the author column from. Null or
    // empty when no failed build names anyone, and then the column is not drawn.
    IReadOnlyList<string>? Authors = null);
