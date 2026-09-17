/// <summary>
/// Projects a <see cref="SessionState"/> into the frame to draw. Pure, and the owner of every
/// composed string: what a row says, what the header counts, what the tray offers. The clock
/// is a parameter so the snapshots are the same every day.
/// </summary>
static class ScreenBuilder
{
    /// <summary>
    /// Border, title, separator, separator, footer, border: the lines around the body.
    /// </summary>
    public const int Chrome = 6;

    public const string Title = "BuildMonitor";

    public static Screen Build(SessionState state, DateTimeOffset now)
    {
        // Once for the tray, the rows and the columns, each of which used to sort every build itself.
        var builds = RowProjection.Builds(state);
        var tray = Tray(state, builds);
        var status = Status(state, now);
        return state.Page switch
        {
            Page.Builds when state.Hidden => HiddenScreen(state, builds, tray, status),
            Page.Builds => BuildsScreen(state, now, builds, tray, status),
            _ => FormScreen(state, tray, status)
        };
    }

    /// <summary>
    /// The builds page without its rows while the window is hidden. No one sees them, and the tray
    /// and the notification, all that is read meanwhile, need none, yet every poll composed and
    /// sized each row of a large account. Showing the window changes the state, which rebuilds the
    /// page whole.
    /// </summary>
    static Screen HiddenScreen(SessionState state, ImmutableArray<Build> builds, TrayModel tray, string status)
    {
        var failing = builds.Count(_ => _.Status == BuildStatus.Failed);
        var running = builds.Count(_ => _.IsActive);
        return new(
            Title,
            Page.Builds,
            new(Header(state, builds.Length, failing, running), [], 0, 0, -1, failing, running, [], [], [], false, state.Search, ""),
            null,
            Buttons(state),
            status,
            tray,
            state.Columns,
            state.Rows,
            null,
            state.Notification,
            state.Settings.Theme);
    }

    static Screen BuildsScreen(SessionState state, DateTimeOffset now, ImmutableArray<Build> builds, TrayModel tray, string status)
    {
        var rows = RowProjection.Rows(state, builds);
        var body = MonitorSession.BodyRows(state);
        var top = Math.Clamp(state.ScrollTop, 0, Math.Max(0, rows.Length - body));
        var visible = rows.Skip(top).Take(body).ToList();
        // Across every failed build rather than the visible rows, so a name does not grow and shrink
        // while scrolling past someone who shares it.
        var authors = AuthorNames.Of(builds.Where(_ => _.Status == BuildStatus.Failed).Select(_ => _.Author));
        var composed = new List<BuildRow>(visible.Count);
        for (var index = 0; index < visible.Count; index++)
        {
            composed.Add(Compose(state, visible[index], top + index == state.SelectedRow, now, authors));
        }

        var failing = builds.Count(_ => _.Status == BuildStatus.Failed);
        var running = builds.Count(_ => _.IsActive);
        var selected = state.SelectedRow >= top && state.SelectedRow < top + visible.Count
            ? state.SelectedRow - top
            : -1;
        MenuOverlay? menu = null;
        if (state.Menu is { } open &&
            open.Row >= top &&
            open.Row < top + visible.Count)
        {
            menu = new(open.Row - top, open.Items.Select(_ => _.Label).ToList(), open.Overflow);
        }

        var sized = Sized(state, builds, rows);
        var loading = Loading(state, rows.Length);
        return new(
            Title,
            Page.Builds,
            new(Header(state, builds.Length, failing, running), composed, top, rows.Length, selected, failing, running, Names(sized, RowKind.Build), Names(sized, RowKind.Group), Details(sized), loading, state.Search, Empty(state, rows.Length, loading), authors.Values.Distinct().ToList()),
            null,
            Buttons(state),
            status,
            tray,
            state.Columns,
            state.Rows,
            menu,
            state.Notification,
            state.Settings.Theme);
    }

    /// <summary>
    /// No rows because a connection has not finished its first poll, which for a large account
    /// takes a while, rather than because there is nothing to show: the empty page said the latter.
    /// Only the first poll counts, so an account with no builds does not flash a spinner on every
    /// later poll, and a connection that is failing says so in the footer instead.
    /// </summary>
    static bool Loading(SessionState state, int rows) =>
        rows == 0 &&
        state.Connections.Any(_ => _.LastPolled is null &&
                                   _.Health is ConnectionHealth.Unpolled or ConnectionHealth.Polling);

    /// <summary>
    /// What the body says with no rows. A filter that matches nothing says so, or the list would look
    /// emptied by something else; a first poll still out is said first, since the rows it brings may
    /// match.
    /// </summary>
    static string Empty(SessionState state, int rows, bool loading)
    {
        if (rows > 0)
        {
            return "";
        }

        if (loading)
        {
            return "Loading builds";
        }

        var search = state.Search.Trim();
        return search.Length > 0 ? $"No builds match \"{search}\"" : "Nothing to show yet.";
    }

    /// <summary>
    /// The rows the columns are sized from: those shown and, while a filter is typed, every row it
    /// could show, so the columns hold still rather than jumping with each letter. The rows shown
    /// stay in, because a build the filter lifts out of its closed group was never measured there.
    /// </summary>
    static ImmutableArray<Row> Sized(SessionState state, ImmutableArray<Build> builds, ImmutableArray<Row> rows)
    {
        if (state.Search.Length == 0)
        {
            return rows;
        }

        // The filter box does not narrow the builds, so the ones already sorted serve here too.
        return [..RowProjection.Rows(state with { Search = "" }, builds), ..rows];
    }

    static List<string> Names(ImmutableArray<Row> rows, RowKind kind)
    {
        var names = new List<string>();
        var seen = new HashSet<string>().GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var row in rows)
        {
            if (row.Kind != kind)
            {
                continue;
            }

            // As NameOf says it, without copying a build's name out of its repository's.
            if (row is { Kind: RowKind.Build, Build: { } build })
            {
                AddDistinct(seen, names, BuildExtensions.ShortRepoName(build.RepoName.AsSpan()));
                continue;
            }

            AddDistinct(seen, names, NameOf(row));
        }

        return names;
    }

    /// <summary>
    /// Adds the text unless it is there already, in the order first seen, and makes it a string only
    /// then. The columns are sized from every row, and building each row's text only for the repeats
    /// to be dropped cost every row its strings on every rebuild.
    /// </summary>
    static void AddDistinct(HashSet<string>.AlternateLookup<ReadOnlySpan<char>> seen, List<string> texts, ReadOnlySpan<char> text)
    {
        if (seen.Add(text) &&
            seen.TryGetValue(text, out var added))
        {
            texts.Add(added);
        }
    }

    /// <summary>
    /// What a row's first cell says. One rule for the composed row and for the names the column is
    /// sized from, so the width can not be measured from text the row does not show.
    /// </summary>
    static string NameOf(Row row) =>
        row.Kind switch
        {
            RowKind.Group => row.Group!.Project,
            RowKind.Member => "",
            _ => row.Build!.ShortRepoName()
        };

    /// <summary>
    /// What a click on a row's first cell opens: the run, where the pipeline is named after the
    /// project. The second cell leaves such a pipeline out, so without this the row, an AppVeyor
    /// project's for one, would name its run nowhere a click could reach.
    /// </summary>
    static ChipKind NameLinkOf(Row row)
    {
        if (row is { Kind: RowKind.Build, Build: { } build } && NamedAfterProject(build))
        {
            return ChipKind.Build;
        }

        return ChipKind.None;
    }

    static bool NamedAfterProject(Build build) =>
        BuildExtensions.ShortRepoName(build.RepoName.AsSpan()).Equals(build.PipelineName, StringComparison.OrdinalIgnoreCase);

    static List<string> Details(ImmutableArray<Row> rows)
    {
        var details = new List<string>();
        var seen = new HashSet<string>().GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var row in rows)
        {
            AddDetail(seen, details, row);
        }

        return details;
    }

    /// <summary>
    /// The text <see cref="DetailOf"/> gives the row, written on the stack rather than as runs that
    /// are then joined.
    /// </summary>
    static void AddDetail(HashSet<string>.AlternateLookup<ReadOnlySpan<char>> seen, List<string> details, Row row)
    {
        if (row.Build is not { } build)
        {
            AddDistinct(seen, details, GroupDetail(row));
            return;
        }

        var (pipeline, branch) = DetailParts(build);
        var separator = pipeline.Length > 0 && branch.Length > 0 ? " " : "";
        var length = pipeline.Length + separator.Length + branch.Length;
        var text = length <= 256 ? stackalloc char[length] : new char[length];
        text.TryWrite($"{pipeline}{separator}{branch}", out _);
        AddDistinct(seen, details, text);
    }

    /// <summary>
    /// The pipeline and branch a build's second cell names, either empty when left out. The pipeline
    /// is left out when the provider names it after the repository, as AppVeyor does, rather than
    /// the same name reading in both columns. The name links to the run instead.
    /// </summary>
    static (string Pipeline, string Branch) DetailParts(Build build)
    {
        if (NamedAfterProject(build))
        {
            return ("", build.ShortBranchName());
        }

        return (build.PipelineName, build.ShortBranchName());
    }

    static string GroupDetail(Row row)
    {
        if (row.Group!.Failed)
        {
            return $"{row.Members.Length} failing";
        }

        return $"{row.Members.Length} passing";
    }

    /// <summary>
    /// What a row's second cell says, in runs, one rule for the row and for the details the column is
    /// sized from, as for <see cref="NameOf"/>. The pipeline links to the run and the branch to its
    /// page. A branch the provider gave no page is plain text, as a link that opened nothing would
    /// read as broken.
    /// </summary>
    static List<DetailSpan> DetailOf(Row row)
    {
        if (row.Build is not { } build)
        {
            return [new(GroupDetail(row))];
        }

        var (pipeline, branch) = DetailParts(build);
        var spans = new List<DetailSpan>();
        Append(spans, pipeline, ChipKind.Build);
        Append(spans, branch, build.BranchUrl is null ? ChipKind.None : ChipKind.Branch);
        return spans;
    }

    /// <summary>
    /// Adds a run after a space. Plain text joins the plain text before it, so a head draws no more
    /// runs than the links need.
    /// </summary>
    static void Append(List<DetailSpan> spans, string text, ChipKind link)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0)
        {
            AppendPlain(spans, " ");
        }

        if (link == ChipKind.None)
        {
            AppendPlain(spans, text);
        }
        else
        {
            spans.Add(new(text, link));
        }
    }

    static void AppendPlain(List<DetailSpan> spans, string text)
    {
        if (spans is [.., { Link: ChipKind.None } last])
        {
            spans[^1] = last with { Text = last.Text + text };
            return;
        }

        spans.Add(new(text));
    }

    static string Header(SessionState state, int pipelines, int failing, int running)
    {
        if (state.Connections.Length == 0)
        {
            return "No connections. Open Options to add one.";
        }

        return $"{Plural(pipelines, "pipeline")}, {failing} failing, {running} running";
    }

    static BuildRow Compose(SessionState state, Row row, bool selected, DateTimeOffset now, IReadOnlyDictionary<string, string> authors)
    {
        if (row.Build is not { } build)
        {
            return ComposeGroup(row, row.Group!, selected, now);
        }

        var estimate = Estimator.Estimate(build, state.Medians);
        var (fraction, timing) = Progress.Compute(build, estimate, now);
        // Only the build that broke names anyone: on a pass or a run the name says nothing wrong.
        var author = build is { Status: BuildStatus.Failed, Author: not null } &&
                     authors.TryGetValue(build.Author.Trim(), out var shown)
            ? shown
            : "";
        return new(
            row.Kind,
            build.Status,
            NameOf(row),
            NameLinkOf(row),
            DetailOf(row),
            row.Connection!.Connection.ProviderId,
            fraction,
            timing,
            selected,
            false,
            RowChips.Of(build, state.LocalRepos),
            author);
    }

    /// <summary>
    /// A group's own row: the project, then what a closed group would otherwise hide, how many
    /// builds it holds and how long since the latest. No chips or links: which build they would act
    /// on is ambiguous, so the group is opened first.
    /// </summary>
    static BuildRow ComposeGroup(Row row, GroupKey group, bool selected, DateTimeOffset now)
    {
        var latest = row.Members.MaxBy(_ => _.Finished ?? _.Started ?? _.Queued ?? DateTimeOffset.MinValue)!;
        var (_, timing) = Progress.Compute(latest, null, now);

        return new(
            RowKind.Group,
            group.Failed ? BuildStatus.Failed : BuildStatus.Succeeded,
            NameOf(row),
            ChipKind.None,
            DetailOf(row),
            "",
            -1,
            timing,
            selected,
            row.Expanded,
            []);
    }


    public static IReadOnlyList<Button> Buttons(SessionState state) =>
        state.Page switch
        {
            Page.Builds =>
            [
                new("Refresh", state.Connections.Length > 0, CommandKind.Refresh),
                new("Options", true, CommandKind.OpenOptions),
                new("Filters", true, CommandKind.OpenFilters),
                new("Hide", true, CommandKind.Hide)
            ],
            Page.Connection => ConnectionButtons(state),
            Page.SignIn =>
            [
                new("Cancel", true, CommandKind.CancelSignIn)
            ],
            _ =>
            [
                new("Save", true, CommandKind.Save),
                new("Cancel", true, CommandKind.CancelForm)
            ]
        };

    static List<Button> ConnectionButtons(SessionState state)
    {
        var form = state.Form!;
        var method = ConnectionDraft.Method(form);
        var buttons = new List<Button>(5)
        {
            new("Sign in", method != AuthMethod.Token, CommandKind.SignIn),
            new("Test", true, CommandKind.TestConnection),
            new("Save", true, CommandKind.Save),
            new("Cancel", true, CommandKind.CancelForm)
        };
        if (form.EditingConnectionId is not null)
        {
            buttons.Add(new("Remove", true, CommandKind.RemoveConnection));
        }

        return buttons;
    }

    public static string Status(SessionState state, DateTimeOffset now)
    {
        if (state.Status.Length > 0)
        {
            return state.Status;
        }

        // With no heading per connection, the footer is the one place always on screen that can
        // say a connection is failing, and otherwise its builds would just quietly stop changing.
        var problems = state.Connections
            .Where(_ => _.Health is ConnectionHealth.NeedsAuth or ConnectionHealth.Error or ConnectionHealth.RateLimited)
            .OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (problems.Count > 0)
        {
            var first = problems[0];
            var problem = first.Health == ConnectionHealth.NeedsAuth
                ? $"Sign in required for {first.Connection.Name}"
                : $"{first.Connection.Name}: {first.Describe(now)}";
            if (problems.Count == 1)
            {
                return problem;
            }

            return $"{problem} (+{problems.Count - 1} more)";
        }

        var polled = state.Connections
            .Where(_ => _.LastPolled is not null)
            .Select(_ => _.LastPolled!.Value)
            .DefaultIfEmpty()
            .Max();
        if (polled == default)
        {
            if (state.Connections.Length == 0)
            {
                return "";
            }

            return "Not polled yet";
        }

        return $"Polled {Progress.Age(now - polled)} ago";
    }

    static Screen FormScreen(SessionState state, TrayModel tray, string status)
    {
        var form = state.Page switch
        {
            Page.Options => OptionsForm(state),
            Page.Filters => FiltersForm(state),
            Page.Connection => ConnectionForm(state),
            Page.SignIn => SignInForm(state),
            _ => throw new($"No form for {state.Page}")
        };
        return new(
            Title,
            state.Page,
            null,
            form,
            Buttons(state),
            status,
            tray,
            state.Columns,
            state.Rows,
            Notification: state.Notification,
            Theme: state.Settings.Theme);
    }

    static FormPage OptionsForm(SessionState state)
    {
        var form = state.Form!;
        var fields = new List<Field>
        {
            new(FormFields.RunAtStartup, FieldKind.Checkbox, "Run at startup", form.Value(FormFields.RunAtStartup)),
            new(FormFields.ShowWindowAtStart, FieldKind.Checkbox, "Show the window at startup", form.Value(FormFields.ShowWindowAtStart)),
            new(FormFields.ShowOtherBranches, FieldKind.Checkbox, "Show running builds on other branches", form.Value(FormFields.ShowOtherBranches)),
            new(FormFields.ShowForks, FieldKind.Checkbox, "Show forks and collaborator repositories", form.Value(FormFields.ShowForks)),
            new(FormFields.NotifyOnFailure, FieldKind.Checkbox, "Notify when a build fails", form.Value(FormFields.NotifyOnFailure)),
            new(FormFields.Theme, FieldKind.Select, "Theme", form.Value(FormFields.Theme), Options: Enum.GetNames<Theme>()),
            new(FormFields.PollInterval, FieldKind.Number, "Poll interval (seconds)", form.Value(FormFields.PollInterval)),
            new(FormFields.RunningPollInterval, FieldKind.Number, "Poll interval while a build is running (seconds)", form.Value(FormFields.RunningPollInterval)),
            new(FormFields.HistoryDays, FieldKind.Number, "Show builds from the last (days)", form.Value(FormFields.HistoryDays), Hint: "Running and queued builds always show."),
            new(FormFields.Port, FieldKind.Number, "Local port", form.Value(FormFields.Port), Hint: "Used by the launcher and the MCP server. Takes effect after a restart."),
            new(FormFields.CodeDirectory, FieldKind.Directory, "Code directory", form.Value(FormFields.CodeDirectory), Hint: "Where your checkouts live", Command: CommandKind.BrowseCodeDirectory),
            new("connectionsLabel", FieldKind.Label, "Connections", "")
        };
        foreach (var connection in state.Connections.OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var descriptor = ProviderDescriptors.Get(connection.Connection.ProviderId);
            fields.Add(new(
                FormFields.Connection(connection.Connection.Id),
                FieldKind.EditRow,
                connection.Connection.Name,
                ConnectionSummary(descriptor, connection),
                Command: CommandKind.EditConnection));
        }

        fields.Add(new(FormFields.AddConnection, FieldKind.Button, "Add connection", "", Command: CommandKind.AddConnection));
        fields.Add(new(FormFields.Version, FieldKind.Label, "Version", VersionReader.VersionString));
        fields.Add(new(FormFields.Documentation, FieldKind.Link, "Documentation", "https://github.com/SimonCropp/BuildMonitor"));
        fields.Add(new(FormFields.OpenLogs, FieldKind.Button, "Open logs", "", Command: CommandKind.OpenLogs));
        fields.Add(new(FormFields.RaiseIssue, FieldKind.Button, "Raise issue", "", Command: CommandKind.RaiseIssue));
        fields.Add(new(FormFields.Update, FieldKind.Button, "Update", "", Command: CommandKind.Update));
        AddError(fields, form);
        return new("Options", fields);
    }

    /// <summary>
    /// A connection's line on the options page. One that can only watch says so, as its rows offer
    /// no retry or cancel and nothing on the builds page says why.
    /// </summary>
    static string ConnectionSummary(ProviderDescriptor descriptor, ConnectionState connection)
    {
        var summary = $"{descriptor.Name}, {HealthWord(connection.Health)}";
        if (connection.Access == BuildAccess.Watch)
        {
            return $"{summary}, watch only";
        }

        return summary;
    }

    static string HealthWord(ConnectionHealth health) =>
        health switch
        {
            ConnectionHealth.Ok => "ok",
            ConnectionHealth.Polling => "polling",
            ConnectionHealth.Error => "error",
            ConnectionHealth.NeedsAuth => "sign in required",
            ConnectionHealth.RateLimited => "rate limited",
            _ => "not polled yet"
        };

    static FormPage FiltersForm(SessionState state)
    {
        var form = state.Form!;
        var fields = new List<Field>();
        if (form.Filters.Length == 0)
        {
            fields.Add(new(FormFields.NoFilters, FieldKind.Label, "No filters. Everything is shown.", ""));
        }

        for (var index = 0; index < form.Filters.Length; index++)
        {
            fields.Add(new(FormFields.Filter(index), FieldKind.ListRow, "Exclude", form.Filters[index].Describe(), Command: CommandKind.RemoveFilter));
        }

        fields.Add(new(FormFields.FilterTarget, FieldKind.Select, "Target", form.Value(FormFields.FilterTarget), Options: Enum.GetNames<FilterTarget>()));
        fields.Add(new(FormFields.FilterKind, FieldKind.Select, "Match", form.Value(FormFields.FilterKind), Options: Enum.GetNames<FilterKind>()));
        fields.Add(new(FormFields.FilterText, FieldKind.Text, "Text", form.Value(FormFields.FilterText)));
        fields.Add(new(FormFields.AddFilter, FieldKind.Button, "Add filter", "", Command: CommandKind.AddFilter));
        AddError(fields, form);
        return new("Filters", fields);
    }

    static FormPage ConnectionForm(SessionState state)
    {
        var form = state.Form!;
        var descriptor = ConnectionDraft.Descriptor(form);
        var method = ConnectionDraft.Method(form);
        var fields = new List<Field>
        {
            new(FormFields.Provider, FieldKind.Select, "Provider", descriptor.Name, Options: ProviderDescriptors.All.Select(_ => _.Name).ToList(), Enabled: form.EditingConnectionId is null),
            new(FormFields.Name, FieldKind.Text, "Name", form.Value(FormFields.Name), Hint: descriptor.Name)
        };
        if (descriptor.SelfHosted)
        {
            fields.Add(new(FormFields.Server, FieldKind.Text, "Server", form.Value(FormFields.Server), Hint: descriptor.DefaultServer));
        }

        foreach (var scope in descriptor.Scopes)
        {
            fields.Add(new(FormFields.Scope(scope.Id), FieldKind.Text, scope.Required ? scope.Label : $"{scope.Label} (optional)", form.Value(FormFields.Scope(scope.Id)), Hint: scope.Hint));
        }

        var methods = descriptor.AuthMethods().Select(_ => _.ToString()).ToList();
        if (methods.Count > 1)
        {
            fields.Add(new(FormFields.Auth, FieldKind.Select, "Sign in with", method.ToString(), Options: methods));
        }

        if (descriptor.UserLabel is not null)
        {
            fields.Add(new(FormFields.User, FieldKind.Text, descriptor.UserLabel, form.Value(FormFields.User)));
        }

        if (method == AuthMethod.Token)
        {
            fields.Add(new(FormFields.Token, FieldKind.Password, descriptor.TokenLabel, form.Value(FormFields.Token), Hint: form.SignedIn ? "Leave empty to keep the stored token" : null));
            fields.Add(new(FormFields.TokenHelp, FieldKind.Link, $"How to get {Article(descriptor.TokenLabel)} {descriptor.TokenLabel}", descriptor.TokenHelpUrl));
        }
        else if (descriptor.CustomClientId)
        {
            fields.Add(new(FormFields.ClientId, FieldKind.Text, "OAuth application id (optional)", form.Value(FormFields.ClientId), Hint: "Only for a self hosted server with its own application"));
        }

        if (method == AuthMethod.Browser)
        {
            fields.Add(new(FormFields.CallbackPort, FieldKind.Number, "Callback port (optional)", form.Value(FormFields.CallbackPort), Hint: "Only when the application was registered with a fixed redirect port"));
        }

        if (descriptor.Notes is not null)
        {
            fields.Add(new(FormFields.Notes, FieldKind.Label, "", descriptor.Notes.Replace("{server}", form.Value(FormFields.Server))));
        }

        fields.Add(new(FormFields.ProviderDocs, FieldKind.Link, $"{descriptor.Name} documentation", descriptor.DocsUrl));

        if (form.Message is not null)
        {
            fields.Add(new(FormFields.Message, FieldKind.Label, "", form.Message));
        }

        AddError(fields, form);
        return new(form.EditingConnectionId is null ? "Add connection" : "Edit connection", fields);
    }

    static FormPage SignInForm(SessionState state)
    {
        var signIn = state.SignIn!;
        var fields = new List<Field>
        {
            new(FormFields.SignInMessage, FieldKind.Label, "", signIn.Message)
        };
        if (signIn.UserCode is not null)
        {
            fields.Add(new(FormFields.UserCode, FieldKind.Label, "Code", signIn.UserCode));
        }

        if (signIn.VerificationUrl is not null)
        {
            fields.Add(new(FormFields.VerificationUrl, FieldKind.Link, signIn.VerificationUrl, signIn.VerificationUrl));
        }

        return new($"Sign in to {signIn.Connection.Name}", fields);
    }

    static void AddError(List<Field> fields, FormState form)
    {
        if (form.Error is not null)
        {
            fields.Add(new("error", FieldKind.Label, "Error", form.Error));
        }
    }

    // Tray

    public static TrayModel Tray(SessionState state) =>
        Tray(state, RowProjection.Builds(state));

    static TrayModel Tray(SessionState state, ImmutableArray<Build> builds)
    {
        var failing = builds.Count(_ => _.Status == BuildStatus.Failed);
        var running = builds.Count(_ => _.IsActive);
        var icon = Icon(state, builds);
        var tooltip = state.Connections.Length == 0
            ? "BuildMonitor: no connections"
            : $"BuildMonitor: {failing} failing, {running} running";

        List<TrayMenuItem> items =
        [
            new(TrayMenu.Open, "Open", IconName: "open"),
            new(TrayMenu.Refresh, "Refresh", Enabled: state.Connections.Length > 0, IconName: "refresh"),
            new(TrayMenu.Options, "Options", IconName: "options"),
            new(TrayMenu.Filters, "Filters", IconName: "filters")
        ];
        // Above Open logs, the other item that opens a folder. Left out entirely rather than
        // disabled where the option is unset: an item that opens nothing is worse than one that is
        // not there, and most users never set it.
        if (state.Settings.CodeDirectory.Length > 0)
        {
            items.Add(new(TrayMenu.CodeDirectory, "Open code directory", IconName: "folder"));
        }

        items.AddRange(
        [
            new(TrayMenu.Logs, "Open logs", IconName: "logs"),
            new(TrayMenu.Issue, "Raise issue", IconName: "issue"),
            new(TrayMenu.Update, "Update", IconName: "update"),
            new(TrayMenu.Exit, "Exit", IconName: "exit")
        ]);
        return new(icon, tooltip, items);
    }

    static TrayIconKind Icon(SessionState state, ImmutableArray<Build> builds)
    {
        if (state.Connections.Any(_ => _.Health is ConnectionHealth.NeedsAuth or ConnectionHealth.Error))
        {
            return TrayIconKind.Attention;
        }

        if (builds.Any(_ => _.Status == BuildStatus.Failed))
        {
            return TrayIconKind.Failed;
        }

        if (builds.Any(_ => _.IsActive))
        {
            return TrayIconKind.Running;
        }

        if (builds.Length > 0 &&
            builds.All(_ => _.Status == BuildStatus.Succeeded))
        {
            return TrayIconKind.Success;
        }

        return TrayIconKind.Idle;
    }

    static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    static string Article(string noun) =>
        "AEIOUaeiou".Contains(noun[0]) ? "an" : "a";
}
