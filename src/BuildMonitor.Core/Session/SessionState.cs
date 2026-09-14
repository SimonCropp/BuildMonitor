/// <summary>
/// The whole app, as one immutable value. <see cref="MonitorSession"/> maps one of these to the
/// next; <see cref="SessionHost"/> guards the single mutable reference; <see cref="ScreenBuilder"/>
/// projects it into a frame. Nothing in here touches IO.
/// <para>
/// Rows are not stored: <see cref="RowProjection.Rows"/> derives them from the builds, the
/// filters and the toggled groups, so there is exactly one rule about what is shown and every reader of
/// <see cref="SelectedRow"/> agrees on what it indexes.
/// </para>
/// </summary>
record SessionState(
    Settings Settings,
    ImmutableArray<ConnectionState> Connections,
    ImmutableArray<Build> Builds,
    // Pipeline key to median duration of recent successful runs, from DurationHistory.
    ImmutableDictionary<string, TimeSpan> Medians,
    Page Page,
    FormState? Form,
    SignInState? SignIn,
    MenuState? Menu,
    int SelectedRow,
    int ScrollTop,
    int Columns,
    int Rows,
    // Group ids the user opened or closed against their default. See RowProjection.IsExpanded.
    ImmutableHashSet<string> ToggledGroups,
    // The last thing worth telling the user, shown on the status line until the next input.
    string Status,
    bool Hidden,
    bool Exit,
    // Waiting for the loop to hand it to the tray. Null once shown.
    Notification? Notification = null)
{
    public static SessionState Start(Settings settings) =>
        new(
            Settings: settings,
            Connections: [..settings.Connections.Select(ConnectionState.Start)],
            Builds: [],
            Medians: [],
            Page: Page.Builds,
            Form: null,
            SignIn: null,
            Menu: null,
            SelectedRow: 0,
            ScrollTop: 0,
            Columns: 120,
            Rows: 30,
            ToggledGroups: [],
            Status: "",
            Hidden: !settings.ShowWindowAtStart,
            Exit: false);

    public ConnectionState? Connection(string id) =>
        Connections.FirstOrDefault(_ => _.Connection.Id == id);
}
