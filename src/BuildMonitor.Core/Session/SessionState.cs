/// <summary>
/// The whole app, as one immutable value. <see cref="MonitorSession"/> maps one of these to the
/// next; <see cref="SessionHost"/> guards the single mutable reference; <see cref="ScreenBuilder"/>
/// projects it into a frame. Nothing in here touches IO.
/// <para>
/// Rows are not stored: <see cref="RowProjection.Rows"/> derives them from the builds, the
/// filters and the open groups, so there is exactly one rule about what is shown and every reader of
/// <see cref="SelectedRow"/> agrees on what it indexes.
/// </para>
/// </summary>
record SessionState(
    Settings Settings,
    ImmutableArray<ConnectionState> Connections,
    ImmutableArray<Build> Builds,
    // Pipeline key to median duration of recent successful runs, from DurationHistory.
    ImmutableDictionary<string, TimeSpan> Medians,
    // Repository name to the directory it is checked out in, from LocalRepoWatcher. See
    // LocalRepos.Index for what a repository is keyed by.
    ImmutableDictionary<string, string> LocalRepos,
    Page Page,
    FormState? Form,
    SignInState? SignIn,
    MenuState? Menu,
    int SelectedRow,
    int ScrollTop,
    int Columns,
    int Rows,
    // The last thing worth telling the user, shown on the status line until the next input.
    string Status,
    bool Hidden,
    bool Exit,
    // The runs a triage is still collecting, as they were when it started, so the row's chip and
    // the footer can say so for as long as the download takes. Only the end of it puts anything on
    // the clipboard, and without this the next click cleared the one line saying it had not yet.
    ImmutableArray<Build> Triaging,
    // Waiting for the loop to hand it to the tray. Null once shown.
    Notification? Notification = null,
    // Text waiting for the loop to put on the clipboard, which belongs to the window and so to the
    // loop's thread rather than the thread pool a log is fetched on. Null once copied.
    PendingCopy? Clipboard = null,
    // What is typed in the filter box: only builds whose project, pipeline or branch contain it are
    // rows. Never saved, unlike Settings.Filters, which exclude for good.
    string Search = "",
    // The exclude or deferral the status line has just reported, which the Undo beside it takes back. Gone with
    // that message, on the next input that is not the Undo itself.
    ExcludeUndo? Undo = null,
    // The positions the last poll changed, and when, so a click on one of them that came too soon
    // after is not taken as meant for the build now there.
    MovedRows? Moved = null,
    // The button of a row the pointer is on, which holds the rows still until it is clicked.
    HoverState? Hover = null)
{
    public static SessionState Start(Settings settings) =>
        new(
            Settings: settings,
            Connections: [..settings.Connections.Select(ConnectionState.Start)],
            Builds: [],
            Medians: [],
            LocalRepos: ImmutableDictionary<string, string>.Empty,
            Page: Page.Builds,
            Form: null,
            SignIn: null,
            Menu: null,
            SelectedRow: 0,
            ScrollTop: 0,
            Columns: 120,
            Rows: 30,
            Status: "",
            Hidden: !settings.ShowWindowAtStart,
            Exit: false,
            Triaging: []);

    public ConnectionState? Connection(string id) =>
        Connections.FirstOrDefault(_ => _.Connection.Id == id);

    /// <summary>
    /// Whether the rows must not move: a context menu is open on one, or the pointer is on a button
    /// of one and has not clicked it yet. A poll re-sorts the list, so <see cref="ConnectionPoller"/>
    /// holds its cycle back while this is true, rather than take the row the user is aiming at out
    /// from under the pointer. <see cref="ConnectionPoller.HoldLimit"/> caps how long.
    /// <para>
    /// Never while hidden: nothing is on screen to be aimed at, and a window hidden with the
    /// pointer on a chip reports no move off it, so the last hover would hold every poll back
    /// until it timed out.
    /// </para>
    /// </summary>
    public bool HoldsRows =>
        !Hidden &&
        (Menu is not null ||
         Hover is {Clicked: false});
}
