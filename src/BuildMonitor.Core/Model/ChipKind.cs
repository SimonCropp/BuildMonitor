/// <summary>
/// What a clickable part of a build's row does. A head reports the kind rather than a position in
/// the row, so a poll that gives the row a chip between the frame drawn and the click cannot turn
/// the click into its neighbour. The chips that are drawn come in the order of these values, links
/// then actions, which is what lets a drop down hold "every chip from this one on"; the kinds that
/// are never drawn as a chip sit outside that order and may be added at the end.
/// Keep in sync with BmChipKind in bm.h.
/// </summary>
enum ChipKind
{
    None = 0,
    // The pipeline's name in the row's text, and the status square, which is the only part an
    // AppVeyor row has once its pipeline is left out: opens the run. A link rather than a chip, so
    // never among a row's chips.
    Build = 1,
    // The branch's name in the row's text: opens the branch. Likewise never among a row's chips.
    Branch = 2,
    PullRequest = 3,
    Retry = 4,
    Cancel = 5,
    CopyLog = 6,
    // The provider icon, which opens the pipeline's page on the CI service. Clicked like a chip,
    // never among a row's chips.
    Pipeline = 7,
    // Opens the local checkout of the build's repository in the file manager. Only on a row whose
    // repository was found under the code directory, so it is the one chip a poll cannot add.
    OpenDirectory = 8,

    // Downloads the build's artifacts and its log to a local directory and copies a prompt naming
    // both. Only on a failed row whose repository was found under the code directory, so like
    // OpenDirectory it is not a chip a poll on its own can add.
    Triage = 9,

    // The row's name, which opens the source repository. Added after the actions rather than beside
    // the other links because the numbers are the wire format: renumbering them would have every
    // committed native binary report the wrong kind until it was rebuilt.
    Repo = 10,

    // Moves a queued build to the front of its service's queue. Here rather than next to Cancel,
    // where it belongs by what it does, for the reason Repo is here: the numbers are the wire
    // format. It is drawn, so it is the last chip of the row that has it; the only chips a queued
    // row can carry beside it are the pull request, Cancel and the checkout.
    RunNext = 11
}
