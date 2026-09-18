/// <summary>
/// Everything the user can ask for, whichever surface asked: a key, a footer button, a context
/// menu item or a tray menu item. Keys reported by a head are the subset in BmKey in bm.h.
/// </summary>
enum CommandKind
{
    None,
    ScrollUp,
    ScrollDown,
    PageUp,
    PageDown,
    ScrollHome,
    ScrollEnd,
    NextRow,
    PreviousRow,
    OpenBuild,
    OpenBranch,
    OpenPullRequest,
    OpenProject,
    CopyBuildUrl,
    CopyLog,
    Retry,
    Cancel,
    OpenRepoDirectory,
    OpenCodeDirectory,
    BrowseCodeDirectory,
    Refresh,
    ToggleGroup,
    ExcludePipeline,
    ExcludeBranch,
    ExcludeRepo,
    OpenBuilds,
    OpenOptions,
    OpenFilters,
    AddConnection,
    EditConnection,
    RemoveConnection,
    AddFilter,
    RemoveFilter,
    SignIn,
    CancelSignIn,
    TestConnection,
    Save,
    CancelForm,
    OpenLogs,
    RaiseIssue,
    // Opens the update page, which says what the update is about to take down.
    Update,
    // The update page's own button: the one that actually takes the tray away.
    ConfirmUpdate,
    Hide,
    Quit,
    CopyStatus
}
