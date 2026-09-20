/// <summary>
/// A part of a build's row that has something of its own to say on hover. A head draws the cells,
/// so it knows which part it is over without mapping back from a click; a chip carries its own text
/// on <see cref="RowChip.Tooltip"/> instead, since which chips a row has is decided per row.
/// Keep in sync with BmRowPart in bm.h.
/// </summary>
enum RowPart
{
    // Anywhere the row does not name something more specific: what the row cannot say for itself,
    // the whole repository name, the commit and who wrote it.
    Row = 0,
    Status = 1,
    Name = 2,
    Provider = 3,
    Pipeline = 4,
    Branch = 5,
    Timing = 6
}
