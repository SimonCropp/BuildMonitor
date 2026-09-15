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
    // width these want before the pipeline and branch are cut short.
    IReadOnlyList<string> Details,
    // No rows yet because a connection has not finished its first poll, so a head draws a spinner
    // rather than an empty page.
    bool Loading);
