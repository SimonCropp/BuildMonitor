/// <summary>
/// Everything the user can ask for, whichever surface asked: a key, a footer button, a context
/// menu item or a tray menu item. Keys reported by a head are the subset in BmKey in bm.h.
/// </summary>
public enum CommandKind
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
    // The pipeline's page on the CI service, and the source repository: two pages, because one
    // command for both had the provider icon open whichever page its provider happened to know.
    OpenPipeline,
    OpenRepo,
    CopyBuildUrl,
    CopyLog,
    // Gathers a failed build's log and artifacts, then copies a prompt naming them.
    Triage,
    Retry,
    Cancel,
    // Moves a queued build to the front of its service's queue.
    RunNext,
    OpenRepoDirectory,
    OpenCodeDirectory,
    BrowseCodeDirectory,
    Refresh,
    ToggleGroup,
    // The selected row's context menu, from the keyboard: Shift+F10 or the Menu key.
    OpenMenu,
    // Adds the menu item's prefix to the ones passing builds are grouped by, or takes it back off.
    GroupByPrefix,
    RemoveGroupPrefix,
    ExcludePipeline,
    ExcludeBranch,
    ExcludeRepo,
    ExcludeOrg,
    // Takes back the exclude the status line reports, and is only offered while it does.
    UndoExclude,
    OpenBuilds,
    OpenConnections,
    OpenOptions,
    OpenFilters,
    AddConnection,
    EditConnection,
    // Opens the editor of the first connection that needs the user, from the footer's button.
    EditUnhealthyConnection,
    // Opens the page that says what removing the edited connection takes with it.
    RemoveConnection,
    // That page's own button: the one that actually forgets the connection and its credential.
    ConfirmRemoveConnection,
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
    CopyStatus,
    // The device flow's code, which the sign in page cannot let the user select.
    CopyUserCode
}
