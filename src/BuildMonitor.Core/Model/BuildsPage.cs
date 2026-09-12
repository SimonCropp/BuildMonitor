record BuildsPage(
    string Header,
    IReadOnlyList<BuildRow> Rows,
    int ScrollTop,
    // All rows, not the visible slice, so a scrollbar knows its travel.
    int TotalRows,
    // Index into Rows of the selected one, or -1 when it is scrolled out of view.
    int SelectedRow,
    int FailingCount,
    int RunningCount);
