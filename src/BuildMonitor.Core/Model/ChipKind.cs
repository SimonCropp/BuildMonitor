/// <summary>
/// What a clickable part of a build's row does. A head reports the kind rather than a position in
/// the row, so a poll that gives the row a chip between the frame drawn and the click cannot turn
/// the click into its neighbour. Chips are drawn in the order of these values, links then actions,
/// which is what lets a drop down hold "every chip from this one on".
/// Keep in sync with BmChipKind in bm.h.
/// </summary>
enum ChipKind
{
    None = 0,
    Build = 1,
    Branch = 2,
    PullRequest = 3,
    Retry = 4,
    Cancel = 5,
    CopyLog = 6,
    // The provider icon, which opens the project page. Clicked like a chip, never among a row's chips.
    Project = 7
}
