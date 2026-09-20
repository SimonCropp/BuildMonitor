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
            Page.Builds when state.Hidden => HiddenScreen(state, now, builds, tray, status),
            Page.Builds => BuildsScreen(state, now, builds, tray, status),
            _ => FormScreen(state, now, tray, status)
        };
    }

    /// <summary>
    /// The builds page without its rows while the window is hidden. No one sees them, and the tray
    /// and the notification, all that is read meanwhile, need none, yet every poll composed and
    /// sized each row of a large account. Showing the window changes the state, which rebuilds the
    /// page whole.
    /// </summary>
    static Screen HiddenScreen(SessionState state, DateTimeOffset now, ImmutableArray<Build> builds, TrayModel tray, string status)
    {
        var failing = builds.Count(_ => _.Status == BuildStatus.Failed);
        var running = builds.Count(_ => _.IsActive);
        return new(
            Title,
            Page.Builds,
            new(Header(state, builds.Length, failing, running), [], 0, 0, -1, failing, running, [], [], [], false, state.Search, SearchTooltip, ""),
            null,
            Buttons(state),
            status,
            StatusTooltip(state, status, now),
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
            new(Header(state, builds.Length, failing, running), composed, top, rows.Length, selected, failing, running, Names(sized, RowKind.Build), Names(sized, RowKind.Group), Details(sized), loading, state.Search, SearchTooltip, Empty(state, rows.Length, loading), authors.Values.Distinct().ToList()),
            null,
            Buttons(state),
            status,
            StatusTooltip(state, status, now),
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
        var seen = new HashSet<string>().GetAlternateLookup<CharSpan>();
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
    static void AddDistinct(HashSet<string>.AlternateLookup<CharSpan> seen, List<string> texts, CharSpan text)
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
    /// What a click on a row's first cell opens. A broken or running build's row leads with the
    /// run: it is the reason the row is being read, so the first thing on it is the thing to open.
    /// A settled row leads with the repository, which is what its cell names.
    /// <para>
    /// The name is the repository's either way. It is the run's title as much as the project's,
    /// and a row that renamed its first cell by status would be unreadable as a column.
    /// </para>
    /// </summary>
    static ChipKind NameLinkOf(Row row)
    {
        if (row is not { Kind: RowKind.Build, Build: { } build })
        {
            return ChipKind.None;
        }

        if (build.NeedsAttention())
        {
            return ChipKind.Build;
        }

        if (build.RepoUrl is not null)
        {
            return ChipKind.Repo;
        }

        return ChipKind.None;
    }

    /// <summary>
    /// The mark before a row's first cell: the service that ran the build where the cell opens the
    /// run, and the host of the source where it opens the repository. A member's first cell is
    /// blank, and a mark before nothing would only say what the row above already does.
    /// </summary>
    static string NameIconOf(Row row, ProviderDescriptor descriptor)
    {
        if (row is not { Kind: RowKind.Build, Build: { } build })
        {
            return "";
        }

        if (build.NeedsAttention())
        {
            return $"provider-{descriptor.Id}";
        }

        return RepoHosts.MarkOf(build.RepoUrl);
    }

    /// <summary>
    /// The mark leading the second cell: whichever of the two the first cell did not take.
    /// </summary>
    static (string Icon, ChipKind Link) DetailIconOf(Row row, ProviderDescriptor descriptor)
    {
        if (row.Build is not { } build)
        {
            return ("", ChipKind.None);
        }

        if (build.NeedsAttention())
        {
            // No mark for a host nothing here has one for, and then no link either: a link on a
            // cell with no picture in it is a rectangle of nothing that reports a click.
            if (RepoHosts.MarkOf(build.RepoUrl) is { Length: > 0 } mark)
            {
                return (mark, ChipKind.Repo);
            }

            return ("", ChipKind.None);
        }

        return ($"provider-{descriptor.Id}", ChipKind.Pipeline);
    }

    static bool NamedAfterProject(Build build) =>
        BuildExtensions.ShortRepoName(build.RepoName.AsSpan()).Equals(build.PipelineName, StringComparison.OrdinalIgnoreCase);

    static List<string> Details(ImmutableArray<Row> rows)
    {
        var details = new List<string>();
        var seen = new HashSet<string>().GetAlternateLookup<CharSpan>();
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
    static void AddDetail(HashSet<string>.AlternateLookup<CharSpan> seen, List<string> details, Row row)
    {
        if (row.Build is null)
        {
            AddDistinct(seen, details, GroupDetail(row));
            return;
        }

        var (pipeline, branch) = DetailParts(row);
        var separator = pipeline.Length > 0 && branch.Length > 0 ? " " : "";
        var length = pipeline.Length + separator.Length + branch.Length;
        var text = length <= 256 ? stackalloc char[length] : new char[length];
        text.TryWrite($"{pipeline}{separator}{branch}", out _);
        AddDistinct(seen, details, text);
    }

    /// <summary>
    /// The pipeline and branch a build's second cell names, either empty when left out. The pipeline
    /// is left out only where the first cell is already showing that name, as it is on an AppVeyor
    /// row whose project is named after its repository. A member of a group has no first cell to
    /// repeat, so its pipeline stays: without it the row named its run nowhere a click could reach.
    /// </summary>
    static (string Pipeline, string Branch) DetailParts(Row row)
    {
        var build = row.Build!;
        if (row.Kind == RowKind.Build && NamedAfterProject(build))
        {
            return ("", build.ShortBranchName());
        }

        return (build.PipelineName, build.ShortBranchName());
    }

    static string GroupDetail(Row row) =>
        $"{row.Members.Length} passing";

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

        var (pipeline, branch) = DetailParts(row);
        var spans = new List<DetailSpan>();
        // The run where the first cell did not take it, and the pipeline's own page where it did,
        // so that no part of a row repeats the one beside it. A row that leads with its run and
        // leaves the pipeline out, because the first cell already names it, has nothing left to
        // carry that page; it is the least of the four, and the run's own page links to it.
        Append(spans, pipeline, build.NeedsAttention() ? ChipKind.Pipeline : ChipKind.Build);
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
            return ComposeGroup(state, row, row.Group!, selected, now);
        }

        var estimate = Estimator.Estimate(build, state.Medians);
        var (fraction, timing) = Progress.Compute(build, estimate, now);
        // Only the build that broke names anyone: on a pass or a run the name says nothing wrong.
        var author = build is { Status: BuildStatus.Failed, Author: not null } &&
                     authors.TryGetValue(build.Author.Trim(), out var shown)
            ? shown
            : "";
        var descriptor = ProviderDescriptors.Get(row.Connection!.Connection.ProviderId);
        var detailIcon = DetailIconOf(row, descriptor);
        return new(
            row.Kind,
            build.Status,
            NameOf(row),
            NameLinkOf(row),
            NameIconOf(row, descriptor),
            ChipKind.Build,
            DetailOf(row),
            detailIcon.Icon,
            detailIcon.Link,
            descriptor.Id,
            fraction,
            timing,
            selected,
            false,
            RowChips.Of(build, descriptor, state.LocalRepos),
            RowTooltips.Of(state, build, descriptor.Name, now),
            author);
    }

    /// <summary>
    /// A group's own row: the project, then what a closed group would otherwise hide, how many
    /// builds it holds and how long since the latest. No links, and no chip that acts on a build:
    /// which member it would act on is ambiguous, so the group is opened first. The one exception
    /// is the checkout, when every member is the same one: a closed group would otherwise hide the
    /// folder button of rows that all name the same folder.
    /// </summary>
    static BuildRow ComposeGroup(SessionState state, Row row, GroupKey group, bool selected, DateTimeOffset now)
    {
        var latest = row.Members.MaxBy(_ => _.Finished ?? _.Started ?? _.Queued ?? DateTimeOffset.MinValue)!;
        var (_, timing) = Progress.Compute(latest, null, now);
        var shared = LocalRepos.Shared(state.LocalRepos, row.Members);
        var repo = RowTooltips.Shared(row.Members);
        return new(
            RowKind.Group,
            // Only passes are grouped, so a group's square is always green.
            BuildStatus.Succeeded,
            NameOf(row),
            // A member's own first cell is blank, so this row is the only place the repository is
            // named. Without the link a group would hide the repository of every row inside it.
            repo is null ? ChipKind.None : ChipKind.Repo,
            RepoHosts.MarkOf(repo),
            ChipKind.None,
            DetailOf(row),
            "",
            ChipKind.None,
            "",
            -1,
            timing,
            selected,
            row.Expanded,
            shared is null ? [] : [new(ChipKind.OpenDirectory, "Open dir", shared, "folder")],
            RowTooltips.OfGroup(group, row.Members, latest, now));
    }


    /// <summary>
    /// What the filter box matches against. The box carries no label, so this is the only place
    /// that says the text is tried against three different parts of a row.
    /// </summary>
    public const string SearchTooltip = "Filter by repository, pipeline or branch";

    /// <summary>
    /// Every failing connection, for a hover on the footer. The footer is one line beside the
    /// buttons and names only the first problem, and the error it carries is usually longer than
    /// the line, so what a connection is actually complaining about is the part that is cut off.
    /// Empty where nothing is wrong: a tooltip repeating "Polled 5s ago" would pop over every
    /// hover of the footer without adding anything.
    /// </summary>
    static string StatusTooltip(SessionState state, string status, DateTimeOffset now)
    {
        if (status.Length == 0)
        {
            return "";
        }

        var problems = state.Connections
            .Where(_ => _.Health is ConnectionHealth.NeedsAuth or ConnectionHealth.Error or ConnectionHealth.RateLimited)
            .OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase)
            .Select(_ => _.Health == ConnectionHealth.NeedsAuth
                ? $"Sign in required for {_.Connection.Name}"
                : $"{_.Connection.Name}: {_.Describe(now)}");
        // A line each, and each wrapped: a provider's error can be a paragraph of its own.
        return Tooltips.Wrap(string.Join("\n", problems));
    }

    public static IReadOnlyList<Button> Buttons(SessionState state) =>
        state.Page switch
        {
            Page.Builds =>
            [
                new("Refresh", state.Connections.Length > 0, CommandKind.Refresh, "Poll every connection now (F5)"),
                new("Options", true, CommandKind.OpenOptions, "Connections, polling and what the window shows"),
                new("Filters", true, CommandKind.OpenFilters, "Hide pipelines for good"),
                new("Hide", true, CommandKind.Hide, "Hide the window; the tray keeps running")
            ],
            Page.Connection => ConnectionButtons(state),
            Page.SignIn => SignInButtons(state),
            Page.Update =>
            [
                new("Update", true, CommandKind.ConfirmUpdate, "Close BuildMonitor, update it and start it again"),
                new("Cancel", true, CommandKind.CancelForm)
            ],
            _ =>
            [
                new("Save", true, CommandKind.Save),
                new("Cancel", true, CommandKind.CancelForm)
            ]
        };

    /// <summary>
    /// Copy code comes first, and only once there is a code: it is what the page is for, and a
    /// button that copied nothing would read as the code having been lost.
    /// </summary>
    static List<Button> SignInButtons(SessionState state)
    {
        var buttons = new List<Button>(2);
        if (state.SignIn?.UserCode is not null)
        {
            buttons.Add(new("Copy code", true, CommandKind.CopyUserCode));
        }

        buttons.Add(new("Cancel", true, CommandKind.CancelSignIn));
        return buttons;
    }

    static List<Button> ConnectionButtons(SessionState state)
    {
        var form = state.Form!;
        var method = ConnectionDraft.Method(form);
        var buttons = new List<Button>(5)
        {
            new("Sign in", method != AuthMethod.Token, CommandKind.SignIn),
            new("Test", true, CommandKind.TestConnection, "Check the server and credential without saving"),
            new("Save", true, CommandKind.Save),
            new("Cancel", true, CommandKind.CancelForm)
        };
        if (form.EditingConnectionId is not null)
        {
            buttons.Add(new("Remove", true, CommandKind.RemoveConnection, "Forget this connection and its stored credential"));
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

    static Screen FormScreen(SessionState state, DateTimeOffset now, TrayModel tray, string status)
    {
        var form = state.Page switch
        {
            Page.Options => OptionsForm(state),
            Page.Filters => FiltersForm(state),
            Page.Connection => ConnectionForm(state),
            Page.SignIn => SignInForm(state),
            Page.Update => UpdateForm(state, now),
            _ => throw new($"No form for {state.Page}")
        };
        return new(
            Title,
            state.Page,
            null,
            form,
            Buttons(state),
            status,
            StatusTooltip(state, status, now),
            tray,
            state.Columns,
            state.Rows,
            Notification: state.Notification,
            Theme: state.Settings.Theme);
    }

    /// <summary>
    /// What the update is about to do, before it does it. The tray exits so that the update can
    /// replace its own files, and nothing is on screen until the new one starts, so this page and
    /// the notification the new one shows are the whole of what the user sees of an update.
    /// </summary>
    static FormPage UpdateForm(SessionState state, DateTimeOffset now)
    {
        var servers = state.Form!.Servers;
        var fields = new List<Field>
        {
            new(FormFields.Version, FieldKind.Label, "Version", VersionReader.VersionString),
            new(FormFields.UpdateSummary, FieldKind.Label, "", "BuildMonitor closes, updates and starts again. Nothing is on screen while it does.")
        };
        foreach (var line in Servers(servers))
        {
            fields.Add(new(FormFields.UpdateServers, FieldKind.Label, "", line));
        }

        foreach (var server in servers.Running)
        {
            fields.Add(new(
                FormFields.McpServer,
                FieldKind.Label,
                "",
                $"- {ShimPath.Command} mcp, process {server.ProcessId}, started {Progress.Age(now - server.Started)} ago"));
        }

        return new("Update", fields);
    }

    /// <summary>
    /// What the servers found mean for the update, which is not the same on every platform: only
    /// Windows has to stop them, so only there is this a warning rather than a note.
    /// <para>
    /// A line each rather than a paragraph, because a label is drawn on one line and cut off at the
    /// window's edge: the sentence that says what the user is about to lose is the last one that
    /// should go.
    /// </para>
    /// </summary>
    static IEnumerable<string> Servers(McpServers servers)
    {
        var count = servers.Running.Length;
        if (count == 0)
        {
            yield return "No MCP server is running, so the update takes nothing else with it.";
            yield break;
        }

        var named = count == 1 ? "1 MCP server" : $"{count} MCP servers";
        if (!servers.StoppedByUpdate)
        {
            yield return $"{named} {(count == 1 ? "keeps" : "keep")} running, on this version, until the AI client using {(count == 1 ? "it" : "each")} connects again:";
            yield break;
        }

        yield return $"Updating stops {named}, because a running one holds the files the update replaces.";
        yield return "An AI client using one loses it mid-conversation, and starts a new one when it next connects:";
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
            new(FormFields.CodeDirectory, FieldKind.Directory, "Code directory", form.Value(FormFields.CodeDirectory), Hint: "Where your checkouts live"),
            new("connectionsLabel", FieldKind.Label, "Connections", "")
        };
        foreach (var connection in state.Connections.OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var descriptor = ProviderDescriptors.Get(connection.Connection.ProviderId);
            fields.Add(new(
                FormFields.Connection(connection.Connection.Id),
                FieldKind.EditRow,
                connection.Connection.Name,
                ConnectionSummary(descriptor, connection)));
        }

        fields.Add(new(FormFields.AddConnection, FieldKind.Button, "Add connection", ""));
        fields.Add(new(FormFields.Version, FieldKind.Label, "Version", VersionReader.VersionString));
        fields.Add(new(FormFields.Documentation, FieldKind.Link, "Documentation", "https://github.com/SimonCropp/BuildMonitor"));
        fields.Add(new(FormFields.OpenLogs, FieldKind.Button, "Open logs", ""));
        fields.Add(new(FormFields.RaiseIssue, FieldKind.Button, "Raise issue", ""));
        fields.Add(new(FormFields.Update, FieldKind.Button, "Update", ""));
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
            fields.Add(new(FormFields.Filter(index), FieldKind.ListRow, "Exclude", form.Filters[index].Describe()));
        }

        fields.Add(new(FormFields.FilterTarget, FieldKind.Select, "Target", form.Value(FormFields.FilterTarget), Options: Enum.GetNames<FilterTarget>()));
        fields.Add(new(FormFields.FilterKind, FieldKind.Select, "Match", form.Value(FormFields.FilterKind), Options: Enum.GetNames<FilterKind>()));
        fields.Add(new(FormFields.FilterText, FieldKind.Text, "Text", form.Value(FormFields.FilterText)));
        fields.Add(new(FormFields.AddFilter, FieldKind.Button, "Add filter", ""));
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

        // Under the method it is about, and only for the methods it is about: the note exists to be
        // read before the sign in is started rather than after the provider has refused the address.
        if (method != AuthMethod.Token &&
            descriptor.SignInNote is not null)
        {
            fields.Add(new(FormFields.SignInNote, FieldKind.Label, "", descriptor.SignInNote));
        }

        if (descriptor.UserLabel is not null)
        {
            fields.Add(new(FormFields.User, FieldKind.Text, descriptor.UserLabel, form.Value(FormFields.User)));
        }

        if (method == AuthMethod.Token)
        {
            fields.Add(new(FormFields.Token, FieldKind.Password, descriptor.TokenLabel, form.Value(FormFields.Token), Hint: form.SignedIn ? "Leave empty to keep the stored token" : null));
            fields.Add(new(FormFields.TokenHelp, FieldKind.Link, $"How to get {Article.For(descriptor.TokenLabel)} {descriptor.TokenLabel}", descriptor.TokenHelpUrl));
        }
        else if (descriptor.CustomClientId)
        {
            fields.Add(new(FormFields.ClientId, FieldKind.Text, "OAuth application id (optional)", form.Value(FormFields.ClientId), Hint: "Only for a self hosted server with its own application"));
        }

        if (method == AuthMethod.Browser)
        {
            fields.Add(new(FormFields.CallbackPort, FieldKind.Number, "Callback port (optional)", form.Value(FormFields.CallbackPort), Hint: "Only when the application was registered with a fixed redirect port"));
        }

        // Only for the method it is about, like the sign in note. Every one of these names a scope or
        // permission to tick while creating a token, which a browser or device sign in never asks the
        // user for: shown there it read as something still to be done before the sign in would work.
        if (method == AuthMethod.Token &&
            descriptor.TokenNote is not null)
        {
            fields.Add(new(FormFields.TokenNote, FieldKind.Label, "", descriptor.TokenNote.Replace("{server}", form.Value(FormFields.Server))));
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

}
