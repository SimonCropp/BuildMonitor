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
        var tray = Tray(state);
        var status = Status(state, now);
        return state.Page switch
        {
            Page.Builds => BuildsScreen(state, now, tray, status),
            _ => FormScreen(state, tray, status)
        };
    }

    static Screen BuildsScreen(SessionState state, DateTimeOffset now, TrayModel tray, string status)
    {
        var rows = RowProjection.Rows(state);
        var body = MonitorSession.BodyRows(state);
        var top = Math.Clamp(state.ScrollTop, 0, Math.Max(0, rows.Length - body));
        var visible = rows.Skip(top).Take(body).ToList();
        var composed = new List<BuildRow>(visible.Count);
        for (var index = 0; index < visible.Count; index++)
        {
            composed.Add(Compose(state, visible[index], top + index == state.SelectedRow, now));
        }

        var builds = rows.Where(_ => _.Build is not null).Select(_ => _.Build!).ToList();
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
            menu = new(open.Row - top, open.Items.Select(_ => _.Label).ToList());
        }

        return new(
            Title,
            Page.Builds,
            new(Header(state, builds.Count, failing, running), composed, top, rows.Length, selected, failing, running),
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

    static string Header(SessionState state, int pipelines, int failing, int running)
    {
        if (state.Connections.Length == 0)
        {
            return "No connections. Open Options to add one.";
        }

        return $"{Plural(pipelines, "pipeline")}, {failing} failing, {running} running";
    }

    static BuildRow Compose(SessionState state, Row row, bool selected, DateTimeOffset now)
    {
        var connection = row.Connection;
        if (row.Build is not { } build)
        {
            var health = connection.Describe(now);
            var label = health.Length == 0 ? connection.Connection.Name : $"{connection.Connection.Name} ({health})";
            return new(
                RowKind.Header,
                BuildStatus.Unknown,
                label,
                "",
                "",
                "",
                -1,
                "",
                selected,
                row.Folded,
                null,
                null,
                null,
                false,
                false,
                health,
                connection.Connection.Name);
        }

        var estimate = Estimator.Estimate(build, state.Medians);
        var (fraction, timing) = Progress.Compute(build, estimate, now);
        // The last segment only: the owner is the same for most of a connection's rows and the
        // full name is in the tooltip.
        var repo = build.RepoName[(build.RepoName.LastIndexOf('/') + 1)..];
        var repoBranch = build.Branch is null
            ? repo
            : $"{repo} {build.Branch}";
        return new(
            RowKind.Build,
            build.Status,
            build.PipelineName,
            repoBranch,
            build.RunNumber.Length == 0 ? "" : $"#{build.RunNumber}",
            build.StatusText ?? build.Status.ToString().ToLowerInvariant(),
            fraction,
            timing,
            selected,
            false,
            new(LinkKind.Build, "Build", build.BuildUrl),
            build.BranchUrl is null ? null : new(LinkKind.Branch, "Branch", build.BranchUrl),
            build.PullRequestUrl is null
                ? null
                : new(LinkKind.PullRequest, build.PullRequestNumber is null ? "PR" : $"PR {build.PullRequestNumber}", build.PullRequestUrl),
            build.CanRetry,
            build.CanCancel,
            Tooltip(build, now),
            connection.Connection.Name);
    }

    /// <summary>
    /// What the row cannot say for itself: the whole repository name, the commit, its author,
    /// when it started.
    /// </summary>
    static string Tooltip(Build build, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            build.Branch is null ? build.RepoName : $"{build.RepoName} {build.Branch}"
        };
        if (build.CommitMessage is not null)
        {
            lines.Add(build.CommitMessage.Split('\n')[0].Trim());
        }

        var details = new List<string>();
        if (build.Author is not null)
        {
            details.Add(build.Author);
        }

        if (build.CommitSha is not null)
        {
            details.Add(build.CommitSha.Length > 7 ? build.CommitSha[..7] : build.CommitSha);
        }

        if (details.Count > 0)
        {
            lines.Add(string.Join(" ", details));
        }

        if (build.Started is { } started)
        {
            lines.Add($"started {Progress.Age(now - started)} ago");
        }

        return string.Join("\n", lines);
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

    static IReadOnlyList<Button> ConnectionButtons(SessionState state)
    {
        var form = state.Form!;
        var method = ConnectionDraft.Method(form);
        var buttons = new List<Button>
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

    static string Status(SessionState state, DateTimeOffset now)
    {
        if (state.Status.Length > 0)
        {
            return state.Status;
        }

        var attention = state.Connections.FirstOrDefault(_ => _.Health == ConnectionHealth.NeedsAuth);
        if (attention is not null)
        {
            return $"Sign in required for {attention.Connection.Name}";
        }

        var polled = state.Connections
            .Where(_ => _.LastPolled is not null)
            .Select(_ => _.LastPolled!.Value)
            .DefaultIfEmpty()
            .Max();
        if (polled == default)
        {
            return state.Connections.Length == 0 ? "" : "Not polled yet";
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
            new(FormFields.NotifyOnFailure, FieldKind.Checkbox, "Notify when a build fails", form.Value(FormFields.NotifyOnFailure)),
            new(FormFields.Theme, FieldKind.Select, "Theme", form.Value(FormFields.Theme), Options: Enum.GetNames<Theme>()),
            new(FormFields.PollInterval, FieldKind.Number, "Poll interval (seconds)", form.Value(FormFields.PollInterval)),
            new(FormFields.RunningPollInterval, FieldKind.Number, "Poll interval while a build is running (seconds)", form.Value(FormFields.RunningPollInterval)),
            new(FormFields.Port, FieldKind.Number, "Local port", form.Value(FormFields.Port), Hint: "Used by the launcher and the MCP server. Takes effect after a restart."),
            new("connectionsLabel", FieldKind.Label, "Connections", "")
        };
        foreach (var connection in state.Connections.OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var descriptor = ProviderDescriptors.Get(connection.Connection.ProviderId);
            fields.Add(new(
                FormFields.Connection(connection.Connection.Id),
                FieldKind.ListRow,
                connection.Connection.Name,
                $"{descriptor.Name}, {HealthWord(connection.Health)}",
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

    public static TrayModel Tray(SessionState state)
    {
        var rows = RowProjection.Rows(state);
        var builds = state.Connections
            .Select(_ => _.Connection.Id)
            .SelectMany(_ => RowProjection.Builds(state, _))
            .ToList();
        var failing = builds.Count(_ => _.Status == BuildStatus.Failed);
        var running = builds.Count(_ => _.IsActive);
        var icon = Icon(state, builds);
        var tooltip = state.Connections.Length == 0
            ? "BuildMonitor: no connections"
            : $"BuildMonitor: {failing} failing, {running} running";

        var items = new List<TrayMenuItem>();
        var shown = 0;
        var overflow = false;
        foreach (var connection in rows.Where(_ => _.Kind == RowKind.Header).Select(_ => _.Connection))
        {
            var interesting = RowProjection.Builds(state, connection.Connection.Id)
                .Where(_ => _.Status == BuildStatus.Failed || _.IsActive)
                .ToList();
            if (interesting.Count == 0)
            {
                continue;
            }

            items.Add(new($"header:{connection.Connection.Id}", connection.Connection.Name, Enabled: false));
            foreach (var build in interesting)
            {
                if (shown >= TrayMenu.MaxBuilds)
                {
                    overflow = true;
                    break;
                }

                shown++;
                var children = new List<TrayMenuItem>
                {
                    new(TrayMenu.BuildItem(build, TrayMenu.OpenAction), "Open build", IconName: "build")
                };
                if (build.CanRetry)
                {
                    children.Add(new(TrayMenu.BuildItem(build, TrayMenu.RetryAction), "Retry", IconName: "retry"));
                }

                if (build.CanCancel)
                {
                    children.Add(new(TrayMenu.BuildItem(build, TrayMenu.CancelAction), "Cancel", IconName: "cancel"));
                }

                var label = build.Branch is null
                    ? $"{build.PipelineName} {build.RunNumberLabel()} {build.Status.ToString().ToLowerInvariant()}"
                    : $"{build.PipelineName} {build.Branch} {build.RunNumberLabel()} {build.Status.ToString().ToLowerInvariant()}";
                items.Add(new(TrayMenu.BuildItem(build, TrayMenu.OpenAction), label.Trim(), IconName: build.Status.ToString().ToLowerInvariant(), Children: children));
            }

            if (overflow)
            {
                items.Add(new(TrayMenu.Overflow, $"Only {TrayMenu.MaxBuilds} builds shown", Enabled: false));
                break;
            }
        }

        if (items.Count > 0)
        {
            items.Add(new("separator", "", Separator: true));
        }

        items.Add(new(TrayMenu.Open, "Open", IconName: "open"));
        items.Add(new(TrayMenu.Refresh, "Refresh", Enabled: state.Connections.Length > 0, IconName: "refresh"));
        items.Add(new(TrayMenu.Options, "Options", IconName: "options"));
        items.Add(new(TrayMenu.Filters, "Filters", IconName: "filters"));
        items.Add(new(TrayMenu.Logs, "Open logs", IconName: "logs"));
        items.Add(new(TrayMenu.Issue, "Raise issue", IconName: "issue"));
        items.Add(new(TrayMenu.Update, "Update", IconName: "update"));
        items.Add(new(TrayMenu.Exit, "Exit", IconName: "exit"));
        return new(icon, tooltip, items);
    }

    static TrayIconKind Icon(SessionState state, List<Build> builds)
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

        if (builds.Count > 0 &&
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
