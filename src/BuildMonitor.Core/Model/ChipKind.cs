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
    // The pipeline's name in the row's text, or the project's where the pipeline is named after it:
    // opens the run. A link in the text rather than a chip, so never among a row's chips.
    Build = 1,
    // The branch's name in the row's text: opens the branch. Likewise never among a row's chips.
    Branch = 2,
    PullRequest = 3,
    Retry = 4,
    Cancel = 5,
    CopyLog = 6,
    // The provider icon, which opens the project page. Clicked like a chip, never among a row's chips.
    Project = 7,
    // Opens the local checkout of the build's repository in the file manager. Only on a row whose
    // repository was found under the code directory, so it is the one chip a poll cannot add.
    OpenDirectory = 8
}
