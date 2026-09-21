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
            StatusTooltip(state, now),
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
            menu = new(open.Row - top, open.Items.Select(_ => new MenuEntry(_.Label, _.SeparatorAbove)).ToList(), open.Overflow);
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
            StatusTooltip(state, now),
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
    /// What the body says with no rows. With no connections nothing could fill it, so it says what
    /// will, above the footer's Add connection. A filter that matches nothing says so, or the list
    /// would look emptied by something else; a first poll still out is said first, since the rows it
    /// brings may match.
    /// </summary>
    static string Empty(SessionState state, int rows, bool loading)
    {
        if (rows > 0)
        {
            return "";
        }

        if (state.Connections.Length == 0)
        {
            return "Add a connection to start watching builds.";
        }

        if (loading)
        {
            return "Loading builds";
        }

        var search = state.Search.Trim();
        if (search.Length > 0)
        {
            return $"No builds match \"{search}\"";
        }

        return "Nothing to show yet.";
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

    /// <summary>
    /// The distinct first cells a column of that kind is sized from. A member is measured with the
    /// builds rather than the groups: its cell is drawn like a build's, and under a prefix group it
    /// holds a repository name the column has to have room for.
    /// </summary>
    static List<string> Names(ImmutableArray<Row> rows, RowKind kind)
    {
        var names = new List<string>();
        var seen = new HashSet<string>().GetAlternateLookup<CharSpan>();
        foreach (var row in rows)
        {
            if (row.Kind != kind &&
                !(kind == RowKind.Build && row.Kind == RowKind.Member))
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
            RowKind.Member => MemberName(row),
            _ => row.Build!.ShortRepoName()
        };

    /// <summary>
    /// A member's first cell is blank under a group named for its repository, since the row above
    /// already says it. Under a prefix group the members are repositories of their own, so each
    /// names itself: a column of pipeline names said nothing about which repository ran which.
    /// </summary>
    static string MemberName(Row row)
    {
        var project = row.Build!.ShortRepoName();
        if (string.Equals(project, row.Group!.Project, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return project;
    }

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
        // A member that names its own repository opens it like any other row: under a prefix group
        // the name is the only thing saying which repository ran the pipeline beside it, and plain
        // text there made the same build read as less than it does outside the group. A member
        // whose cell is blank has nothing to open, and the group's row above carries the link.
        if (row.Build is not { } build ||
            NameOf(row).Length == 0)
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
    /// run, and the host of the source where it opens the repository. Nothing before a blank cell,
    /// which is a member under a group named for its repository: a mark there would only say what
    /// the row above already does.
    /// </summary>
    static string NameIconOf(Row row, ProviderDescriptor descriptor)
    {
        if (row.Build is not { } build ||
            NameOf(row).Length == 0)
        {
            return "";
        }

        if (build.NeedsAttention())
        {
            return ProviderMarks.Run(descriptor.Id, build.Status);
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

        return (ProviderMarks.Pipeline(descriptor.Id), ChipKind.Pipeline);
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
    /// row whose project is named after its repository, or on a member naming its own project under
    /// a group the server or a prefix made. A member with a blank first cell has nothing to repeat,
    /// so its pipeline stays: without it the row named its run nowhere a click could reach.
    /// </summary>
    static (string Pipeline, string Branch) DetailParts(Row row)
    {
        var build = row.Build!;
        if (NameOf(row).Length > 0 &&
            NamedAfterProject(build))
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
            return "No connections";
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
    /// Whatever the footer had to shorten, for a hover on it. The footer is one line beside the
    /// buttons and names only the first of what it found, and the error a connection carries is
    /// usually longer than the line, so what it is actually complaining about is the part that is
    /// cut off. Every line the footer counted with "(+2 more)" is here, or the count would point
    /// at nothing.
    /// <para>
    /// Empty where nothing is wrong and no poll is out: a tooltip repeating "Polled 5s ago" would
    /// pop over every hover of the footer without adding anything.
    /// </para>
    /// </summary>
    static string StatusTooltip(SessionState state, DateTimeOffset now)
    {
        var lines = Problems(state, now);
        if (lines.Count == 0)
        {
            lines = Polls(state, now);
        }

        if (lines.Count == 0)
        {
            return "";
        }

        // A line each, and each wrapped: a provider's error can be a paragraph of its own.
        return Tooltips.Wrap(string.Join("\n", lines));
    }

    static List<string> Problems(SessionState state, DateTimeOffset now) =>
    [
        ..MonitorSession.Unhealthy(state)
            .Select(_ => _.Health == ConnectionHealth.NeedsAuth
                ? $"Sign in required for {_.Connection.Name}"
                : $"{_.Connection.Name}: {_.Describe(now)}")
    ];

    /// <summary>
    /// Every connection part way through a poll someone is waiting on. Only a refresh and a first
    /// poll set <see cref="ConnectionHealth.Polling"/>, so this is empty through the scheduled
    /// cycles that run all day, and the footer does not flicker between them.
    /// </summary>
    static List<string> Polls(SessionState state, DateTimeOffset now) =>
    [
        ..state.Connections
            .Where(_ => _.Health == ConnectionHealth.Polling)
            .OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase)
            .Select(_ => $"{_.Connection.Name}: {_.Describe(now)}")
    ];

    /// <summary>
    /// One line out of several: the first, and a count of the rest, which the tooltip lists in
    /// full.
    /// </summary>
    static string FirstOf(List<string> lines)
    {
        if (lines.Count == 1)
        {
            return lines[0];
        }

        return $"{lines[0]} (+{lines.Count - 1} more)";
    }

    public static IReadOnlyList<Button> Buttons(SessionState state) =>
        state.Page switch
        {
            Page.Builds => BuildsButtons(state),
            Page.SignIn => SignInButtons(state),
            // By the form rather than the page, and with no fallback: a page left out of a
            // fallback would silently get Save and Cancel, and lose its own buttons.
            _ => state.Form switch
            {
                AddConnectionFormState form => ConnectionButtons(form),
                EditConnectionFormState form => EditConnectionButtons(form),
                UpdateFormState =>
                [
                    new("Update", true, CommandKind.ConfirmUpdate, "Close BuildMonitor, update it and start it again"),
                    new("Cancel", true, CommandKind.CancelForm)
                ],
                // The one Cancel that stays in the editor rather than leaving for the builds, so it
                // says where it goes.
                RemoveConnectionFormState =>
                [
                    new("Remove", true, CommandKind.ConfirmRemoveConnection, "Forget this connection and delete its stored credential"),
                    new("Cancel", true, CommandKind.CancelForm, "Back to the connection")
                ],
                // Back rather than Cancel: each editor saves its own connection, so leaving this
                // list has nothing to throw away.
                ConnectionsFormState =>
                [
                    new("Add connection", true, CommandKind.AddConnection, "Watch the builds of another CI service"),
                    new("Back", true, CommandKind.CancelForm, "Back to the builds")
                ],
                // About last, beside rather than between the two that decide the edits, and it
                // keeps them: its Back comes to this page as it was left.
                OptionsFormState =>
                [
                    new("Save", true, CommandKind.Save),
                    new("Cancel", true, CommandKind.CancelForm),
                    new("About", true, CommandKind.OpenAbout, "Version, documentation, logs and updates")
                ],
                FiltersFormState =>
                [
                    new("Save", true, CommandKind.Save),
                    new("Cancel", true, CommandKind.CancelForm)
                ],
                AboutFormState =>
                [
                    new("Back", true, CommandKind.CancelForm, "Back to the options")
                ],
                _ => throw new($"No buttons for {state.Page}")
            }
        };

    /// <summary>
    /// A way to act on the connection the footer names, where one needs the user: the footer is
    /// the one place always on screen that says a connection is failing, and reaching its editor
    /// otherwise took the connections page and a click on it there. None for a rate limit, which
    /// the poller waits out by itself.
    /// <para>
    /// With no connections, Add connection stands where Refresh and Connections would, which have
    /// nothing to poll and nothing to list. The empty page used to send a new user to the options,
    /// where the button to add one sat under a dozen settings.
    /// </para>
    /// <para>
    /// Undo only while the status line reports the exclude it takes back, and last: a click is
    /// resolved against the buttons again once that message has gone, and a button leaving from
    /// the middle would put every one after it under a different index.
    /// </para>
    /// </summary>
    static List<Button> BuildsButtons(SessionState state)
    {
        List<Button> buttons = state.Connections.Length == 0
            ? [new("Add connection", true, CommandKind.AddConnection, "Watch the builds of a CI service")]
            :
            [
                new("Refresh", true, CommandKind.Refresh, "Poll every connection now (F5)"),
                new("Connections", true, CommandKind.OpenConnections, "The CI services being watched")
            ];
        buttons.AddRange(
        [
            new("Options", true, CommandKind.OpenOptions, "Polling, startup and what the window shows"),
            new("Filters", true, CommandKind.OpenFilters, "Hide pipelines for good"),
            new("Hide", true, CommandKind.Hide, "Hide the window; the tray keeps running")
        ]);
        if (MonitorSession.NeedingUser(state) is { } unhealthy)
        {
            var name = unhealthy.Connection.Name;
            buttons.Add(
                unhealthy.Health == ConnectionHealth.NeedsAuth
                    ? new("Sign in", true, CommandKind.EditUnhealthyConnection, $"Open {name} to sign in again")
                    : new("Check connection", true, CommandKind.EditUnhealthyConnection, $"Open {name} to check its server and credential"));
        }

        if (state.Undo is { } undo)
        {
            buttons.Add(new("Undo", true, CommandKind.UndoExclude, $"Show the {undo.What} again"));
        }

        return buttons;
    }

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

    static List<Button> ConnectionButtons(ConnectionFormState form) =>
    [
        new("Sign in", ConnectionDraft.Method(form) != AuthMethod.Token, CommandKind.SignIn),
        new("Test", true, CommandKind.TestConnection, "Check the server and credential without saving"),
        new("Save", true, CommandKind.Save),
        new("Cancel", true, CommandKind.CancelForm)
    ];

    /// <summary>
    /// Remove last, after the buttons both editors share, so they sit in the same place on either.
    /// </summary>
    static List<Button> EditConnectionButtons(EditConnectionFormState form) =>
    [
        ..ConnectionButtons(form),
        new("Remove", true, CommandKind.RemoveConnection, "Forget this connection and its stored credential")
    ];

    /// <summary>
    /// The footer's one line. With no heading per connection, this is the one place always on
    /// screen that can say a connection is failing, and otherwise its builds would just quietly
    /// stop changing.
    /// <para>
    /// The message from whatever the user last did comes first, being the only feedback some
    /// actions have. It carries a count of the standing problems rather than replacing them: the
    /// message stays until the next click, so a "Retrying" from this morning used to sit over
    /// "Sign in required for GitHub" all day, which is the reason that retry did nothing.
    /// </para>
    /// </summary>
    public static string Status(SessionState state, DateTimeOffset now)
    {
        var problems = Problems(state, now);
        if (state.Status.Length > 0)
        {
            if (problems.Count == 0)
            {
                return state.Status;
            }

            return $"{state.Status} ({Plural(problems.Count, "problem")})";
        }

        if (problems.Count > 0)
        {
            return FirstOf(problems);
        }

        // Ahead of the last poll's age, which a poll in flight is about to replace anyway, and
        // which says nothing about the wait the user is watching.
        var polls = Polls(state, now);
        if (polls.Count > 0)
        {
            return FirstOf(polls);
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
        // The sign in page is drawn over the connection editor, whose form is still in the state.
        var formPage = state.Page == Page.SignIn
            ? SignInForm(state)
            : state.Form switch
            {
                ConnectionsFormState => ConnectionsForm(state),
                OptionsFormState form => OptionsForm(form),
                AboutFormState => AboutForm(),
                FiltersFormState form => FiltersForm(form),
                AddConnectionFormState form => AddConnectionForm(form),
                EditConnectionFormState form => EditConnectionForm(form),
                UpdateFormState form => UpdateForm(form, now),
                RemoveConnectionFormState form => RemoveConnectionForm(state, form),
                _ => throw new($"No form for {state.Page}")
            };
        return new(
            Title,
            state.Page,
            null,
            formPage,
            Buttons(state),
            status,
            StatusTooltip(state, now),
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
    /// <summary>
    /// What a yes takes away, said before it is given: which connection, its credential and its
    /// builds, and what getting it back costs, the one part nothing here can undo. Named as saved
    /// rather than as the editor may have renamed it, since the saved one is what goes, and a line
    /// each, as on the update page, because a label is drawn on one line and cut at the edge.
    /// </summary>
    static FormPage RemoveConnectionForm(SessionState state, RemoveConnectionFormState form)
    {
        var saved = state.Connection(form.ConnectionId)?.Connection;
        var descriptor = ProviderDescriptors.Get(form.Editor.ProviderId);
        var server = saved?.Server is { Length: > 0 } url ? $"{descriptor.Name}, {url}" : descriptor.Name;
        var method = saved?.Auth ?? ConnectionDraft.Method(form.Editor);
        return new(
            "Remove connection",
            [
                new(FormFields.RemovedConnection, FieldKind.Label, saved?.Name ?? form.Editor.Value(FormFields.Name), server),
                new(FormFields.RemoveSummary, FieldKind.Label, "", "Removing it deletes the credential stored for it, and its builds leave the list."),
                new(
                    FormFields.RemoveReturn,
                    FieldKind.Label,
                    "",
                    method == AuthMethod.Token
                        ? "Adding it back means entering a token again."
                        : "Adding it back means signing in again.")
            ]);
    }

    static FormPage UpdateForm(UpdateFormState form, DateTimeOffset now)
    {
        var servers = form.Servers;
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

    /// <summary>
    /// The settings, under a heading for each part of the app they change, and nothing else, so
    /// everything on the page waits for its Save. Twelve of them in one column read as a list to
    /// search rather than one to scan.
    /// </summary>
    static FormPage OptionsForm(OptionsFormState form)
    {
        var fields = new List<Field>
        {
            Heading(FormFields.GeneralHeading, "General"),
            new(FormFields.RunAtStartup, FieldKind.Checkbox, "Run at startup", form.Value(FormFields.RunAtStartup)),
            new(FormFields.ShowWindowAtStart, FieldKind.Checkbox, "Show the window at startup", form.Value(FormFields.ShowWindowAtStart)),
            new(FormFields.NotifyOnFailure, FieldKind.Checkbox, "Notify when a build fails", form.Value(FormFields.NotifyOnFailure)),
            new(FormFields.Theme, FieldKind.Select, "Theme", form.Value(FormFields.Theme), Options: Enum.GetNames<Theme>()),
            Heading(FormFields.BuildsHeading, "Builds"),
            new(FormFields.ShowOtherBranches, FieldKind.Checkbox, "Show running builds on other branches", form.Value(FormFields.ShowOtherBranches)),
            new(FormFields.ShowForks, FieldKind.Checkbox, "Show forks and collaborator repositories", form.Value(FormFields.ShowForks)),
            new(FormFields.HistoryDays, FieldKind.Number, "Show builds from the last (days)", form.Value(FormFields.HistoryDays), Note: "Running and queued builds always show."),
            new(FormFields.GroupPrefixes, FieldKind.Text, "Group passing builds by prefix", form.Value(FormFields.GroupPrefixes), Hint: "Comma separated, eg TheProject"),
            Heading(FormFields.PollingHeading, "Polling"),
            new(FormFields.PollInterval, FieldKind.Number, "Poll interval (seconds)", form.Value(FormFields.PollInterval)),
            new(FormFields.RunningPollInterval, FieldKind.Number, "Poll interval while a build is running (seconds)", form.Value(FormFields.RunningPollInterval)),
            Heading(FormFields.LocalHeading, "Local"),
            new(FormFields.CodeDirectory, FieldKind.Directory, "Code directory", form.Value(FormFields.CodeDirectory), Hint: "Where your checkouts live"),
            new(FormFields.Port, FieldKind.Number, "Local port", form.Value(FormFields.Port), Note: "Used by the launcher and the MCP server. Takes effect after a restart.")
        };
        AddError(fields, form);
        return new("Options", fields);
    }

    /// <summary>
    /// A heading over the fields below it: a label with no value, which every head draws alone.
    /// </summary>
    static Field Heading(string id, string text) =>
        new(id, FieldKind.Label, text, "");

    /// <summary>
    /// What acts the moment it is clicked. Among the options, under a footer saying Save and
    /// Cancel, it read as waiting for the Save, and Update left for its own page and dropped
    /// whatever had been typed.
    /// </summary>
    static FormPage AboutForm() =>
        new(
            "About",
            [
                new(FormFields.Version, FieldKind.Label, "Version", VersionReader.VersionString),
                new(FormFields.Documentation, FieldKind.Link, "Documentation", "https://github.com/SimonCropp/BuildMonitor"),
                new(FormFields.OpenLogs, FieldKind.Button, "Open logs", ""),
                new(FormFields.RaiseIssue, FieldKind.Button, "Raise issue", ""),
                new(FormFields.Update, FieldKind.Button, "Update", "")
            ]);

    /// <summary>
    /// Every connection, by name, each a row that opens its editor. Nothing here is saved by the
    /// page: each editor saves its own connection, which is why the footer has Back where the
    /// options have Cancel.
    /// </summary>
    static FormPage ConnectionsForm(SessionState state)
    {
        var fields = new List<Field>();
        if (state.Connections.Length == 0)
        {
            fields.Add(new(FormFields.NoConnections, FieldKind.Label, "", "No connections yet. Add one for each CI service to watch."));
        }

        foreach (var connection in state.Connections.OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var descriptor = ProviderDescriptors.Get(connection.Connection.ProviderId);
            fields.Add(new(
                FormFields.Connection(connection.Connection.Id),
                FieldKind.EditRow,
                connection.Connection.Name,
                ConnectionSummary(descriptor, connection)));
        }

        return new("Connections", fields);
    }

    /// <summary>
    /// A connection's line on the connections page. One that can only watch says so, as its rows
    /// offer no retry or cancel and nothing on the builds page says why.
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

    static FormPage FiltersForm(FiltersFormState form)
    {
        var fields = new List<Field>();
        if (form.Filters.Length == 0)
        {
            fields.Add(new(FormFields.NoFilters, FieldKind.Label, "", "No filters. Everything is shown."));
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

    static FormPage AddConnectionForm(AddConnectionFormState form)
    {
        var provider = new Field(FormFields.Provider, FieldKind.Select, "Provider", form.Descriptor.Name, Options: ProviderDescriptors.All.Select(_ => _.Name).ToList());
        return new("Add connection", ConnectionFields(form, provider));
    }

    /// <summary>
    /// The provider as text rather than a disabled drop down. On an existing connection it is not
    /// a choice being withheld but no choice at all, and a greyed out drop down invited a click
    /// that did nothing.
    /// </summary>
    static FormPage EditConnectionForm(EditConnectionFormState form)
    {
        var provider = new Field(FormFields.Provider, FieldKind.Label, "Provider", form.Descriptor.Name);
        return new("Edit connection", ConnectionFields(form, provider));
    }

    /// <summary>
    /// Everything under the provider, which both editors show the same way.
    /// </summary>
    static List<Field> ConnectionFields(ConnectionFormState form, Field provider)
    {
        var descriptor = form.Descriptor;
        var method = ConnectionDraft.Method(form);
        var fields = new List<Field>
        {
            provider,
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
        return fields;
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
        var icon = Icon(state, builds);
        var tooltip = TrayTooltip(state, builds);

        List<TrayMenuItem> items =
        [
            new(TrayMenu.Open, "Open", IconName: "open"),
            new(TrayMenu.Refresh, "Refresh", Enabled: state.Connections.Length > 0, IconName: "refresh"),
            new(TrayMenu.Connections, "Connections", IconName: "connections"),
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

    /// <summary>
    /// The most a tray tooltip can hold on the tightest of the heads: NotifyIcon refuses more, so
    /// the Windows head cuts it there, which would halve a name rather than drop it.
    /// </summary>
    public const int TrayTooltipLimit = 127;

    /// <summary>
    /// What a hover on the tray icon says, which while the window is hidden is the whole of the app.
    /// A connection that is not working comes first, being what the Attention icon shows: counts
    /// alone put "0 failing, 0 running" beside it, which reads as all clear. Failures are named, as
    /// their rows name them, rather than counted, since which one is the next thing a hover wants.
    /// Names come off the end as "and 2 more" until the line fits in <see cref="TrayTooltipLimit"/>.
    /// </summary>
    static string TrayTooltip(SessionState state, ImmutableArray<Build> builds)
    {
        if (state.Connections.Length == 0)
        {
            return $"{Title}: no connections";
        }

        var failed = builds.Count(_ => _.Status == BuildStatus.Failed);
        var names = builds
            .Where(_ => _.Status == BuildStatus.Failed)
            .Select(_ => _.ShortRepoName())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var running = builds.Count(_ => _.IsActive);
        // What raised the icon leads, as Unhealthy orders them: a rate limit raises no Attention.
        var problems = MonitorSession.Unhealthy(state).Select(TrayProblem).ToList();
        var lead = problems.Count == 0 ? "" : $"{FirstOf(problems)}. ";
        var tooltip = "";
        for (var shown = names.Count; shown >= 0; shown--)
        {
            tooltip = $"{Title}: {lead}{Failing(names, shown, failed)}, {running} running";
            if (tooltip.Length <= TrayTooltipLimit)
            {
                return tooltip;
            }
        }

        // Only a connection named past any sense gets here: cut, but visibly.
        return $"{tooltip[..(TrayTooltipLimit - 1)]}…";
    }

    /// <summary>
    /// A connection that is not working, said short. The error itself is left out: it is usually
    /// a paragraph, and the window has it, so the hover only has to say which connection to look at.
    /// </summary>
    static string TrayProblem(ConnectionState connection) =>
        connection.Health switch
        {
            ConnectionHealth.NeedsAuth => $"sign in required for {connection.Connection.Name}",
            ConnectionHealth.RateLimited => $"{connection.Connection.Name} rate limited",
            _ => $"error polling {connection.Connection.Name}"
        };

    /// <summary>
    /// The first <paramref name="shown"/> of the failing projects and how many more there are, or
    /// with none shown, the count of failed builds the header gives.
    /// </summary>
    static string Failing(List<string> names, int shown, int failed)
    {
        if (shown == 0)
        {
            return $"{failed} failing";
        }

        var listed = names.Take(shown).ToList();
        if (names.Count > shown)
        {
            listed.Add($"{names.Count - shown} more");
        }

        if (listed.Count == 1)
        {
            return $"{listed[0]} failing";
        }

        return $"{string.Join(", ", listed.Take(listed.Count - 1))} and {listed[^1]} failing";
    }

    /// <summary>
    /// Attention is for a connection that may need the user: a sign in, or an error. A rate limit
    /// never does, since the poller waits it out by itself, so it leaves the icon to the builds and
    /// is said only in the tooltip, where a hover asking why the rows have stopped changing finds it.
    /// </summary>
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

    static string Plural(int count, string noun)
    {
        if (count == 1)
        {
            return $"1 {noun}";
        }

        return $"{count} {noun}s";
    }

}
