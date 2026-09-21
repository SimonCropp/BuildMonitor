using RowIdentity = (string? ConnectionId, string? PipelineId, string? Branch, string? Group);

/// <summary>
/// Every transition, as a pure function from one <see cref="SessionState"/> to the next. No IO:
/// the applier decides what to do in the world and calls back in with the result.
/// </summary>
static class MonitorSession
{
    public static SessionState Resize(SessionState state, int columns, int rows) =>
        Clamp(state with { Columns = Math.Max(40, columns), Rows = Math.Max(10, rows) });

    public static int BodyRows(SessionState state) =>
        Math.Max(1, state.Rows - ScreenBuilder.Chrome);

    // Scrolling

    public static SessionState Scroll(SessionState state, int delta) =>
        Clamp(state with { ScrollTop = state.ScrollTop + delta });

    public static SessionState ScrollTo(SessionState state, int top) =>
        Clamp(state with { ScrollTop = top });

    public static SessionState PageUp(SessionState state) =>
        Scroll(state, -BodyRows(state));

    public static SessionState PageDown(SessionState state) =>
        Scroll(state, BodyRows(state));

    public static SessionState ScrollHome(SessionState state) =>
        ScrollTo(state, 0);

    public static SessionState ScrollEnd(SessionState state) =>
        ScrollTo(state, int.MaxValue / 2);

    // Selection

    public static SessionState SelectRow(SessionState state, int row) =>
        EnsureVisible(Clamp(state with { SelectedRow = row }));

    public static SessionState NextRow(SessionState state) =>
        SelectRow(state, state.SelectedRow + 1);

    public static SessionState PreviousRow(SessionState state) =>
        SelectRow(state, state.SelectedRow - 1);

    /// <summary>
    /// Selects the row of the build with that key, and scrolls it into view. Unchanged where the
    /// build has no row: it can have finished, been filtered out, or been retried into a run of
    /// its own between the notification popping and the click on it, and moving the selection
    /// somewhere arbitrary reads worse than leaving it where the user left it.
    /// </summary>
    public static SessionState SelectBuild(SessionState state, string key)
    {
        var rows = RowProjection.Rows(state);
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].Build?.HasKey(key) == true)
            {
                return SelectRow(state, index);
            }
        }

        return state;
    }

    /// <summary>
    /// Scrolls the least amount that brings the selection into the body.
    /// </summary>
    static SessionState EnsureVisible(SessionState state)
    {
        var body = BodyRows(state);
        if (state.SelectedRow < state.ScrollTop)
        {
            return ScrollTo(state, state.SelectedRow);
        }

        if (state.SelectedRow >= state.ScrollTop + body)
        {
            return ScrollTo(state, state.SelectedRow - body + 1);
        }

        return state;
    }

    /// <summary>
    /// Keeps the scroll top and the selection inside the rows that exist. Enough when only the
    /// window, the scroll or the index changed; a transition that changes the rows goes through
    /// <see cref="Follow"/>, or the selection lands on whichever row took its place.
    /// </summary>
    static SessionState Clamp(SessionState state) =>
        Clamp(state, RowProjection.Rows(state).Length);

    /// <summary>
    /// For a caller that has projected the rows already. <see cref="Follow"/> projected them a
    /// third time here, and a poll applies inside the lock the frame loop takes every frame.
    /// </summary>
    static SessionState Clamp(SessionState state, int total)
    {
        var body = BodyRows(state);
        var maxTop = Math.Max(0, total - body);
        var top = Math.Clamp(state.ScrollTop, 0, maxTop);
        var selected = Math.Clamp(state.SelectedRow, 0, Math.Max(0, total - 1));
        if (top == state.ScrollTop &&
            selected == state.SelectedRow)
        {
            return state;
        }

        return state with { ScrollTop = top, SelectedRow = selected };
    }

    /// <summary>
    /// Re-applies the selection to rows that changed. A poll re-sorts the list, running first, and
    /// a closed group, a filter or a removed connection takes rows out, so the row at the selected
    /// index is often a different one afterwards. The selection keeps its row: the same pipeline on
    /// the same branch, else the row that now holds that run, as when a build passes and joins its
    /// project's closed group or a group splits, else the same pipeline when its latest run is on
    /// another branch now, else the nearest row above it that is still shown.
    /// <para>
    /// The menu does not follow. Moved, it would put a different item under the pointer; left in
    /// place, it would sit beside another build. Unless its row is still where it was drawn, it
    /// closes.
    /// </para>
    /// </summary>
    /// <param name="polled">When the change is a poll's, which also records the positions it moved,
    /// from the rows projected here rather than projecting them twice more.</param>
    static SessionState Follow(SessionState before, SessionState after, DateTimeOffset? polled = null)
    {
        var previous = RowProjection.Rows(before);
        var rows = RowProjection.Rows(after);
        var indexes = new Dictionary<RowIdentity, int>();
        for (var index = 0; index < rows.Length; index++)
        {
            indexes.TryAdd(Identity(rows[index]), index);
        }

        var followed = Clamp(after with { SelectedRow = Locate(previous, rows, indexes, before.SelectedRow) }, rows.Length);
        if (followed.Menu is { } menu &&
            (followed.ScrollTop != before.ScrollTop ||
             menu.Row >= previous.Length ||
             !indexes.TryGetValue(Identity(previous[menu.Row]), out var row) ||
             row != menu.Row))
        {
            followed = followed with { Menu = null };
        }

        if (polled is { } now)
        {
            return Moved(before, previous, followed, rows, now);
        }

        return followed;
    }

    static int Locate(ImmutableArray<Row> previous, ImmutableArray<Row> rows, Dictionary<RowIdentity, int> indexes, int selected)
    {
        if (selected < 0 ||
            selected >= previous.Length)
        {
            return selected;
        }

        if (indexes.TryGetValue(Identity(previous[selected]), out var index))
        {
            return index;
        }

        var builds = previous[selected].Builds;
        // A row showing the run itself beats an open group's row, which stands for it too.
        foreach (var build in builds)
        {
            for (var candidate = 0; candidate < rows.Length; candidate++)
            {
                if (rows[candidate].Build is { } shown &&
                    shown.SameKey(build))
                {
                    return candidate;
                }
            }
        }

        foreach (var build in builds)
        {
            for (var candidate = 0; candidate < rows.Length; candidate++)
            {
                if (rows[candidate].Builds.Any(_ => _.SameKey(build)))
                {
                    return candidate;
                }
            }
        }

        foreach (var build in builds)
        {
            for (var candidate = 0; candidate < rows.Length; candidate++)
            {
                if (rows[candidate].Builds.Any(_ => _.SamePipeline(build)))
                {
                    return candidate;
                }
            }
        }

        for (var above = selected - 1; above >= 0; above--)
        {
            if (indexes.TryGetValue(Identity(previous[above]), out index))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// A build is the same row whether it stands alone or under its open group, so a build joining
    /// a group, or a group opening around the selection, keeps the selection on that build. The
    /// key's parts rather than the key, which cost every build's row two strings each time the rows
    /// moved. A group keeps its <see cref="GroupKey.Id"/>, which is what ignores the project's case.
    /// </summary>
    static RowIdentity Identity(Row row)
    {
        if (row.Build is { } build)
        {
            return (build.ConnectionId, build.PipelineId, build.Branch ?? "", null);
        }

        return (null, null, null, row.Group!.Id);
    }

    public static Row? SelectedRow(SessionState state)
    {
        var rows = RowProjection.Rows(state);
        if (state.SelectedRow < 0 ||
            state.SelectedRow >= rows.Length)
        {
            return null;
        }

        return rows[state.SelectedRow];
    }

    public static Build? SelectedBuild(SessionState state) =>
        SelectedRow(state)?.Build;

    // Filter box

    /// <summary>
    /// Narrows the rows to the builds matching what was typed. The selection keeps its build while
    /// that still shows, and is scrolled into view: every letter moves the rows under it, and a
    /// selection left above or below the body reads as lost.
    /// </summary>
    public static SessionState Search(SessionState state, string text)
    {
        if (state.Search == text)
        {
            return state;
        }

        return EnsureVisible(Follow(state, state with { Search = text }));
    }

    // Groups

    /// <summary>
    /// Opens a group or closes it. The selection moves to the group's own row, since the rows
    /// under the cursor change either way and a member row would vanish on closing.
    /// </summary>
    public static SessionState ToggleGroup(SessionState state, GroupKey key)
    {
        var removed = state.OpenGroups.Remove(key.Id);
        var toggled = ReferenceEquals(removed, state.OpenGroups)
            ? state.OpenGroups.Add(key.Id)
            : removed;
        var next = state with { OpenGroups = toggled };
        var rows = RowProjection.Rows(next);
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index] is { Kind: RowKind.Group, Group: { } group } &&
                group.Id == key.Id)
            {
                return SelectRow(next, index);
            }
        }

        return Clamp(next);
    }

    // Context menu

    public static SessionState OpenMenu(SessionState state, int row)
    {
        var rows = RowProjection.Rows(state);
        if (row < 0 ||
            row >= rows.Length)
        {
            return state;
        }

        var items = ImmutableArray.CreateBuilder<MenuItem>();
        var target = rows[row];
        if (target.Kind == RowKind.Group)
        {
            items.Add(new(target.Expanded ? "Collapse" : "Expand", CommandKind.ToggleGroup));
            if (LocalRepos.Shared(state.LocalRepos, target.Members) is not null)
            {
                items.Add(new("Open directory", CommandKind.OpenRepoDirectory));
            }

            items.Add(new("Refresh", CommandKind.Refresh));
            // A group is as good a place to widen the grouping from as a row is: a repository
            // group of one project's workflows is exactly what someone looking at
            // "TheProject.Messages" wants folded into "TheProject".
            AddGrouping(items, state, target.Group!.Project);
            // Where the group is one someone asked for, the row it is on is where they would
            // undo it: the options page holds the list, but nothing there says which row it made.
            if (Prefix(state, target.Group.Project) is { } named)
            {
                items.Add(new($"Stop grouping: {named}", CommandKind.RemoveGroupPrefix, named));
            }
        }
        else if (target.Build is { } build)
        {
            items.Add(new("Open build", CommandKind.OpenBuild));
            if (build.BranchUrl is not null)
            {
                items.Add(new("Open branch", CommandKind.OpenBranch));
            }

            if (build.PullRequestUrl is not null)
            {
                items.Add(new("Open pull request", CommandKind.OpenPullRequest));
            }

            items.Add(new("Copy build URL", CommandKind.CopyBuildUrl));
            if (build.LogCopyable())
            {
                items.Add(new("Copy log", CommandKind.CopyLog));
            }

            if (build.LogCopyable() &&
                LocalRepos.Find(state.LocalRepos, build) is not null)
            {
                items.Add(new("Triage", CommandKind.Triage));
            }

            if (build.Retryable())
            {
                items.Add(new("Retry", CommandKind.Retry));
            }

            if (build.CanCancel)
            {
                items.Add(new("Cancel build", CommandKind.Cancel));
            }

            if (build.CanRunNext(ProviderDescriptors.Get(target.Connection!.Connection.ProviderId)))
            {
                items.Add(new("Run next", CommandKind.RunNext));
            }

            if (LocalRepos.Find(state.LocalRepos, build) is not null)
            {
                items.Add(new("Open directory", CommandKind.OpenRepoDirectory));
            }

            if (target is { Kind: RowKind.Member, Group: { } group })
            {
                items.Add(new($"Collapse {group.Project}", CommandKind.ToggleGroup));
            }

            items.Add(new("Refresh", CommandKind.Refresh));
            AddGrouping(items, state, build.ShortRepoName());
            // What is excluded, then which one of them: a name that runs long, as a repository's
            // does, pushes the word that says what it is off the end of a narrow menu.
            items.Add(new($"Exclude {PipelineNoun(state, build)}: {build.PipelineName}", CommandKind.ExcludePipeline));
            if (build.Branch is { } branch)
            {
                items.Add(new($"Exclude branch: {branch}", CommandKind.ExcludeBranch));
            }

            // Where the provider has nothing above the pipeline, such as Bitbucket and Travis, the
            // repo is the pipeline, and a second item would exclude what the first one does.
            if (build.RepoName != build.PipelineName)
            {
                items.Add(new($"Exclude repo: {build.RepoName}", CommandKind.ExcludeRepo));
            }

            // Offered even where the repo item is not: a service whose pipeline is the repository,
            // such as Bitbucket, still has a workspace above it worth dropping whole.
            if (Org(state, build) is { } org)
            {
                items.Add(new($"Exclude {org.Noun}: {org.Name}", CommandKind.ExcludeOrg));
            }
        }

        return SelectRow(state, row) with { Menu = new(row, Divided(items.ToImmutable())) };
    }

    /// <summary>
    /// A line above each item that does a different kind of thing from the one before it, by
    /// <see cref="Section"/>. Run together, Retry and Cancel build sat between Copy log and Refresh,
    /// and the excludes followed Refresh straight on, one slip from either.
    /// </summary>
    static ImmutableArray<MenuItem> Divided(ImmutableArray<MenuItem> items)
    {
        var divided = items.ToBuilder();
        for (var index = 1; index < divided.Count; index++)
        {
            if (Section(divided[index].Command) != Section(divided[index - 1].Command))
            {
                divided[index] = divided[index] with { SeparatorAbove = true };
            }
        }

        return divided.ToImmutable();
    }

    /// <summary>
    /// The kind of thing a menu item does, in the order the menu offers them: what to look at or
    /// copy, what changes a service's builds, what changes this machine or this window, and what
    /// hides rows for good. A group's menu is all of the third kind, so it has no lines.
    /// </summary>
    static int Section(CommandKind command) =>
        command switch
        {
            CommandKind.Retry or
                CommandKind.Cancel or
                CommandKind.RunNext => 1,
            CommandKind.OpenRepoDirectory or
                CommandKind.ToggleGroup or
                CommandKind.Refresh or
                CommandKind.GroupByPrefix or
                CommandKind.RemoveGroupPrefix => 2,
            CommandKind.ExcludePipeline or
                CommandKind.ExcludeBranch or
                CommandKind.ExcludeRepo or
                CommandKind.ExcludeOrg => 3,
            _ => 0
        };

    /// <summary>
    /// The drop down of the chips a narrow row had no room for: every chip of the build from
    /// <paramref name="from"/> on. Taken from the build as it is now, and by kind rather than by
    /// position, so it never offers a chip the build lost since the row was drawn, such as Cancel
    /// on a build that has finished.
    /// </summary>
    public static SessionState OpenOverflow(SessionState state, int row, ChipKind from)
    {
        var rows = RowProjection.Rows(state);
        if (row < 0 ||
            row >= rows.Length ||
            rows[row].Build is not { } build)
        {
            return state;
        }

        var items = RowChips.Of(build, ProviderDescriptors.Get(rows[row].Connection!.Connection.ProviderId), state.LocalRepos)
            .Where(_ => _.Kind >= from)
            .Select(_ => new MenuItem(_.Label, RowChips.Command(_.Kind)))
            .ToImmutableArray();
        if (items.Length == 0)
        {
            return state;
        }

        return SelectRow(state, row) with { Menu = new(row, items, Overflow: true) };
    }

    public static SessionState CloseMenu(SessionState state)
    {
        if (state.Menu is null)
        {
            return state;
        }

        return state with { Menu = null };
    }

    /// <summary>
    /// The chosen item's command and its <see cref="MenuItem.Target"/>, which is the only thing
    /// left of the menu once it closes: a Group by prefix item is one of several whose command is
    /// the same, and the row they were opened on does not say which was clicked.
    /// </summary>
    public static (SessionState State, CommandKind Command, string? Target) ChooseMenuItem(SessionState state, int index)
    {
        if (state.Menu is null ||
            index < 0 ||
            index >= state.Menu.Items.Length)
        {
            return (CloseMenu(state), CommandKind.None, null);
        }

        var item = state.Menu.Items[index];
        return (CloseMenu(state), item.Command, item.Target);
    }

    // Pages

    public static SessionState OpenBuilds(SessionState state) =>
        state with { Page = Page.Builds, Form = null, SignIn = null, Menu = null };

    public static SessionState OpenConnections(SessionState state) =>
        OpenForm(state, new ConnectionsFormState { Values = [] });

    public static SessionState OpenOptions(SessionState state)
    {
        var settings = state.Settings;
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        values[FormFields.RunAtStartup] = Flag(settings.RunAtStartup);
        values[FormFields.ShowWindowAtStart] = Flag(settings.ShowWindowAtStart);
        values[FormFields.ShowOtherBranches] = Flag(settings.ShowOtherBranches);
        values[FormFields.ShowForks] = Flag(settings.ShowForksAndCollaborations);
        values[FormFields.NotifyOnFailure] = Flag(settings.NotifyOnFailure);
        values[FormFields.GroupPrefixes] = string.Join(", ", settings.GroupPrefixes);
        values[FormFields.Theme] = settings.Theme.ToString();
        values[FormFields.PollInterval] = settings.PollIntervalSeconds.ToString();
        values[FormFields.RunningPollInterval] = settings.RunningPollIntervalSeconds.ToString();
        values[FormFields.HistoryDays] = settings.HistoryDays.ToString();
        values[FormFields.Port] = settings.Port.ToString();
        values[FormFields.CodeDirectory] = settings.CodeDirectory;
        return OpenForm(state, new OptionsFormState { Values = values.ToImmutable() });
    }

    public static SessionState OpenFilters(SessionState state)
    {
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        values[FormFields.FilterKind] = nameof(FilterKind.Prefix);
        values[FormFields.FilterTarget] = nameof(FilterTarget.Pipeline);
        values[FormFields.FilterText] = "";
        return OpenForm(
            state,
            new FiltersFormState
            {
                Values = values.ToImmutable(),
                Filters = state.Settings.Filters
            });
    }

    /// <summary>
    /// The page that stands between asking for an update and getting one. The tray has to exit for
    /// the update to replace its own files, so nothing is on screen for as long as it takes; this is
    /// the one chance to say what is about to happen, and to say what else is running that the
    /// update takes down with it.
    /// </summary>
    public static SessionState OpenUpdate(SessionState state, McpServers servers) =>
        OpenForm(
            state,
            new UpdateFormState
            {
                Values = [],
                Servers = servers,
                About = state.Form as AboutFormState
            });

    /// <summary>
    /// Only from the options, which it holds and goes back to: from anywhere else there would be
    /// no options for its Back to return to.
    /// </summary>
    public static SessionState OpenAbout(SessionState state)
    {
        if (state.Form is not OptionsFormState options)
        {
            return state;
        }

        return OpenForm(
            state,
            new AboutFormState
            {
                Values = [],
                Options = options
            });
    }

    /// <summary>
    /// The page and the form are set together, from the form: set apart they could disagree, and
    /// the window would draw one page while a save acted on another.
    /// </summary>
    static SessionState OpenForm(SessionState state, FormState form) =>
        state with
        {
            Page = form.Page,
            Menu = null,
            Form = form
        };

    /// <param name="draftId">The id the connection will have, assigned now so that a browser sign
    /// in can store its token before the connection is saved.</param>
    public static SessionState OpenAddConnection(SessionState state, string draftId)
    {
        var descriptor = ProviderDescriptors.All[0];
        var values = ConnectionValues(descriptor, null)
            .SetItem(FormFields.Provider, descriptor.Name);
        return OpenForm(
            state,
            new AddConnectionFormState
            {
                Values = values,
                ConnectionId = draftId,
                Connections = state.Form as ConnectionsFormState
            });
    }

    /// <summary>
    /// Nothing opens for a connection that has gone, which a poll can do between the frame being
    /// drawn and the click on its row. Opening a blank editor instead, as this once did, read as
    /// the click having opened the wrong page.
    /// </summary>
    public static SessionState OpenEditConnection(SessionState state, string connectionId)
    {
        if (state.Connection(connectionId)?.Connection is not { } existing)
        {
            return state;
        }

        return OpenForm(
            state,
            new EditConnectionFormState
            {
                Values = ConnectionValues(ProviderDescriptors.Get(existing.ProviderId), existing),
                ConnectionId = existing.Id,
                ProviderId = existing.ProviderId,
                // An existing connection already has its secret stored.
                SignedIn = true,
                Connections = state.Form as ConnectionsFormState
            });
    }

    /// <summary>
    /// Every connection that is not working: those that need the user first, a sign in or an error,
    /// then those rate limited, which the poller waits out by itself, and by name within each. One
    /// order for the footer, its tooltip, its button and the tray, so all of them name the same one
    /// first. By name alone, a rate limit sorting first hid the sign in behind "(+1 more)", beside a
    /// button that could only be about the other.
    /// </summary>
    public static IEnumerable<ConnectionState> Unhealthy(SessionState state) =>
        state.Connections
            .Where(_ => _.Health is ConnectionHealth.NeedsAuth or ConnectionHealth.Error or ConnectionHealth.RateLimited)
            .OrderBy(_ => _.Health == ConnectionHealth.RateLimited)
            .ThenBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The connection the footer's button opens: the first that needs the user, or null where
    /// none does.
    /// </summary>
    public static ConnectionState? NeedingUser(SessionState state) =>
        Unhealthy(state).FirstOrDefault(_ => _.Health != ConnectionHealth.RateLimited);

    /// <summary>
    /// Leaves a form for where it was opened from: a connection editor, or the page asking about
    /// removing its connection, for the connections page it was opened from; the about page for
    /// the options, as they were left; the update page for the about page it was opened from; every
    /// other form, and any of those opened from anywhere else, for the builds page.
    /// </summary>
    public static SessionState CloseForm(SessionState state)
    {
        FormState? back = state.Form switch
        {
            ConnectionFormState editor => editor.Connections,
            RemoveConnectionFormState removing => removing.Editor.Connections,
            AboutFormState about => about.Options,
            UpdateFormState update => update.About,
            _ => null
        };
        if (back is null)
        {
            return OpenBuilds(state);
        }

        return OpenForm(state with { SignIn = null }, back);
    }

    /// <summary>
    /// Asks before the editor's connection goes. Only from the edit page, and only while the
    /// connection is still there: a new connection's draft id is not one to remove, and a page
    /// asking about a connection already gone would take a yes for nothing.
    /// </summary>
    public static SessionState OpenRemoveConnection(SessionState state)
    {
        if (state.Form is not EditConnectionFormState editor ||
            state.Connection(editor.ConnectionId) is null)
        {
            return state;
        }

        return OpenForm(
            state,
            new RemoveConnectionFormState
            {
                Values = [],
                Editor = editor
            });
    }

    /// <summary>
    /// A no to removing: back to the editor it was asked from, as it was left.
    /// </summary>
    public static SessionState CancelRemoveConnection(SessionState state)
    {
        if (state.Form is not RemoveConnectionFormState removing)
        {
            return state;
        }

        return OpenForm(state, removing.Editor);
    }

    static ImmutableDictionary<string, string> ConnectionValues(ProviderDescriptor descriptor, Connection? existing)
    {
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        values[FormFields.Name] = existing?.Name ?? "";
        values[FormFields.Server] = existing?.Server ?? descriptor.DefaultServer ?? "";
        values[FormFields.Auth] = (existing?.Auth ?? descriptor.AuthMethods().First()).ToString();
        values[FormFields.User] = existing?.User ?? "";
        values[FormFields.Token] = "";
        values[FormFields.ClientId] = existing?.ClientId ?? "";
        values[FormFields.CallbackPort] = existing?.CallbackPort?.ToString() ?? "";
        foreach (var scope in descriptor.Scopes)
        {
            values[FormFields.Scope(scope.Id)] = existing?.ScopeValue(scope.Id) ?? "";
        }

        return values.ToImmutable();
    }

    public static SessionState FieldChanged(SessionState state, string id, string value)
    {
        if (state.Form is null)
        {
            return state;
        }

        var form = state.Form.With(id, value);
        // Switching provider re-derives the dependent fields: server, auth method and scopes. Only
        // a new connection has a provider to switch; an existing one's is not among its values.
        if (id == FormFields.Provider &&
            state.Form is AddConnectionFormState &&
            ProviderDescriptors.ByName(value) is { } descriptor)
        {
            form = form
                .With(FormFields.Server, descriptor.DefaultServer ?? "")
                .With(FormFields.Auth, descriptor.AuthMethods().First().ToString());
            foreach (var scope in descriptor.Scopes)
            {
                if (!form.Values.ContainsKey(FormFields.Scope(scope.Id)))
                {
                    form = form.With(FormFields.Scope(scope.Id), "");
                }
            }
        }

        return state with { Form = form with { Error = null } };
    }

    public static SessionState SetFormError(SessionState state, FormError error) =>
        state.Form switch
        {
            // Clears the message too, or a failed test leaves "Testing..." above its error.
            ConnectionFormState form => state with { Form = form with { Error = error, Message = null } },
            { } form => state with { Form = form with { Error = error } },
            null => state
        };

    /// <summary>
    /// Only a connection editor shows a message, so a test result that arrives once the user has
    /// left the editor is dropped rather than written onto a form with nowhere to show it.
    /// </summary>
    public static SessionState SetFormMessage(SessionState state, string message)
    {
        if (state.Form is not ConnectionFormState form)
        {
            return state;
        }

        return state with { Form = form with { Message = message, Error = null } };
    }

    // Filters page

    public static SessionState AddFilter(SessionState state)
    {
        if (state.Form is not FiltersFormState form)
        {
            return state;
        }

        var text = form.Value(FormFields.FilterText).Trim();
        if (text.Length == 0)
        {
            return SetFormError(state, new("Enter the text to match.", FormFields.FilterText));
        }

        if (!Enum.TryParse<FilterKind>(form.Value(FormFields.FilterKind), out var kind) ||
            !Enum.TryParse<FilterTarget>(form.Value(FormFields.FilterTarget), out var target))
        {
            return SetFormError(state, new("Choose a kind and a target.", FormFields.FilterKind));
        }

        var filter = new Filter(kind, target, text);
        if (form.Filters.Contains(filter))
        {
            return SetFormError(state, new("That filter already exists.", FormFields.FilterText));
        }

        var added = form with { Filters = form.Filters.Add(filter), Error = null };
        return state with { Form = added.With(FormFields.FilterText, "") };
    }

    public static SessionState RemoveFilter(SessionState state, int index)
    {
        if (state.Form is not FiltersFormState form ||
            index < 0 ||
            index >= form.Filters.Length)
        {
            return state;
        }

        return state with { Form = form with { Filters = form.Filters.RemoveAt(index), Error = null } };
    }

    /// <summary>
    /// What the build's service calls the thing an exclusion drops: an action, a job, a build
    /// config. Named after the pipeline in the menu and the status, so "Exclude CI" cannot read as
    /// excluding something other than the pipeline it names.
    /// </summary>
    public static string PipelineNoun(SessionState state, Build build) =>
        ProviderDescriptors.PipelineNoun(state.Connection(build.ConnectionId)?.Connection.ProviderId);

    /// <summary>
    /// The service the build ran on, or null when its connection has gone, which a poll between the
    /// frame being drawn and a click on it can do.
    /// </summary>
    public static ProviderDescriptor? Descriptor(SessionState state, Build build) =>
        ProviderDescriptors.Find(state.Connection(build.ConnectionId)?.Connection.ProviderId);

    /// <summary>
    /// The prefixes the name shares with another project, longest first, so a family is grouped
    /// from a row that named it rather than by typing it on the options page. Offered on a build's
    /// row and on a group's alike: a group of one repository's workflows is still a member of
    /// whatever family that repository belongs to.
    /// </summary>
    static void AddGrouping(ImmutableArray<MenuItem>.Builder items, SessionState state, string project)
    {
        foreach (var prefix in PrefixCandidates.Of(project, Projects(state), state.Settings.GroupPrefixes))
        {
            items.Add(new($"Group by prefix: {prefix}", CommandKind.GroupByPrefix, prefix));
        }
    }

    /// <summary>
    /// Every project a row names, which the menu's prefixes are drawn from. Not narrowed by the
    /// filter box: a prefix offered while a filter is typed would otherwise be one of the few
    /// projects left on screen rather than one of the family.
    /// </summary>
    static IReadOnlyCollection<string> Projects(SessionState state)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var build in RowProjection.Builds(state))
        {
            projects.Add(build.ShortRepoName());
        }

        return projects;
    }

    /// <summary>
    /// The configured prefix this group was made by, or null where the group is a repository or a
    /// group the service itself named: neither is something the menu can take back.
    /// </summary>
    static string? Prefix(SessionState state, string project)
    {
        foreach (var prefix in state.Settings.GroupPrefixes)
        {
            if (string.Equals(prefix, project, StringComparison.OrdinalIgnoreCase))
            {
                return prefix;
            }
        }

        return null;
    }

    /// <summary>
    /// The context menu's "Group by prefix": the prefix is added to the settings, so it groups
    /// every passing build that starts with it and holds across restarts.
    /// </summary>
    public static SessionState GroupByPrefix(SessionState state, string prefix)
    {
        if (state.Settings.GroupPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
        {
            return state;
        }

        return ApplySettings(state, state.Settings with { GroupPrefixes = state.Settings.GroupPrefixes.Add(prefix) });
    }

    /// <summary>
    /// The context menu's "Stop grouping", which undoes one prefix and leaves the rest. The
    /// members go back to the rows they had, each under its own repository.
    /// </summary>
    public static SessionState RemoveGroupPrefix(SessionState state, string prefix)
    {
        var kept = state.Settings.GroupPrefixes
            .Where(_ => !string.Equals(_, prefix, StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();
        if (kept.Length == state.Settings.GroupPrefixes.Length)
        {
            return state;
        }

        return ApplySettings(state, state.Settings with { GroupPrefixes = kept });
    }

    /// <summary>
    /// The context menu's "Exclude": an exact filter on the pipeline name, applied at once.
    /// </summary>
    public static SessionState ExcludePipeline(SessionState state, Build build) =>
        Exclude(state, FilterTarget.Pipeline, build.PipelineName, $"{build.PipelineName} {PipelineNoun(state, build)}");

    /// <summary>
    /// The context menu's "Exclude branch", which drops that branch everywhere rather than only on
    /// the pipeline it was asked on: a branch worth hiding, such as a bot's, is worth hiding on all
    /// of them. A build with no branch, as GoCD and Octopus report, has no such item.
    /// </summary>
    public static SessionState ExcludeBranch(SessionState state, Build build)
    {
        if (build.Branch is not { } branch)
        {
            return state;
        }

        return Exclude(state, FilterTarget.Branch, branch, $"{branch} branch");
    }

    /// <summary>
    /// The context menu's "Exclude repo", which takes every pipeline of the repository with it, and
    /// before they are fetched: a repo rule is checked at discovery.
    /// </summary>
    public static SessionState ExcludeRepo(SessionState state, Build build) =>
        Exclude(state, FilterTarget.Repo, build.RepoName, $"{build.RepoName} repo");

    /// <summary>
    /// The org the build's repository sits under and what its service calls one, or null where the
    /// service has no level above the repository. Both parts or neither: a name with no noun would
    /// read as excluding the repository, and a noun with no name has nothing to exclude.
    /// </summary>
    public static (string Noun, string Name)? Org(SessionState state, Build build)
    {
        if (ProviderDescriptors.OrgNoun(state.Connection(build.ConnectionId)?.Connection.ProviderId) is not { } noun ||
            OrgName.Of(build.RepoName) is not { } name)
        {
            return null;
        }

        return (noun, name);
    }

    /// <summary>
    /// The context menu's "Exclude org", which takes every repository of the account with it, and
    /// before their pipelines are discovered: a provider that pays a request per repository to list
    /// them skips an excluded one outright.
    /// </summary>
    public static SessionState ExcludeOrg(SessionState state, Build build)
    {
        if (Org(state, build) is not { } org)
        {
            return state;
        }

        return Exclude(state, FilterTarget.Org, org.Name, $"{org.Name} {org.Noun}");
    }

    /// <param name="what">What the status line calls it, which is also what its Undo says.</param>
    static SessionState Exclude(SessionState state, FilterTarget target, string text, string what)
    {
        var filter = new Filter(FilterKind.Exact, target, text);
        if (state.Settings.Filters.Contains(filter))
        {
            return state;
        }

        var excluded = ApplySettings(state, state.Settings with { Filters = state.Settings.Filters.Add(filter) });
        return excluded with { Undo = new(filter, what) };
    }

    /// <summary>
    /// Takes back the exclude the status line reported. Only the filter it added comes out, so one
    /// the user already had, of the same rows or others, stays.
    /// </summary>
    public static SessionState UndoExclude(SessionState state)
    {
        if (state.Undo is not { } undo)
        {
            return state;
        }

        var restored = ApplySettings(state, state.Settings with { Filters = state.Settings.Filters.Remove(undo.Filter) });
        return restored with { Undo = null };
    }

    /// <summary>
    /// The Undo beside the status line goes when its message does.
    /// </summary>
    public static SessionState ForgetUndo(SessionState state) =>
        state.Undo is null ? state : state with { Undo = null };

    // Settings

    /// <summary>
    /// Swaps in new settings and reconciles the runtime connections with them: a connection that
    /// is still there keeps its health, a new one starts unpolled, a removed one takes its builds
    /// with it.
    /// </summary>
    public static SessionState ApplySettings(SessionState state, Settings settings)
    {
        var connections = settings.Connections
            .Select(_ =>
            {
                var current = state.Connection(_.Id);
                if (current is null)
                {
                    return ConnectionState.Start(_);
                }

                return current with { Connection = _ };
            })
            .ToImmutableArray();
        var ids = settings.Connections.Select(_ => _.Id).ToHashSet();
        return Follow(state, state with
        {
            Settings = settings,
            Connections = connections,
            Builds = [..state.Builds.Where(_ => ids.Contains(_.ConnectionId))]
        });
    }

    public static SessionState AddConnection(SessionState state, Connection connection) =>
        ApplySettings(state, state.Settings with { Connections = state.Settings.Connections.Add(connection) });

    /// <summary>
    /// In place rather than removed and added, so the connection keeps its position in the list
    /// and, through <see cref="ApplySettings"/>, its health and last poll.
    /// </summary>
    public static SessionState ReplaceConnection(SessionState state, Connection connection) =>
        ApplySettings(
            state,
            state.Settings with
            {
                Connections = [..state.Settings.Connections.Select(_ => _.Id == connection.Id ? connection : _)]
            });

    public static SessionState RemoveConnection(SessionState state, string connectionId) =>
        ApplySettings(
            state,
            state.Settings with
            {
                Connections = [..state.Settings.Connections.Where(_ => _.Id != connectionId)]
            });

    // Sign in

    public static SessionState BeginSignIn(SessionState state, Connection connection, AuthMethod method, Guid flowId) =>
        state with
        {
            Page = Page.SignIn,
            SignIn = new(
                connection,
                method,
                flowId,
                method == AuthMethod.Device
                    ? "Requesting a device code..."
                    : "Waiting for the browser. Sign in there and this page will update.",
                null,
                null)
        };

    /// <summary>
    /// The code goes on the clipboard as well as on the page. The page is the only place it exists
    /// and it is drawn as a label, which no toolkit lets the user select, so without this the code
    /// has to be read off the screen and typed into the provider's page character by character.
    /// </summary>
    public static SessionState SignInProgress(SessionState state, Guid flowId, string userCode, string verificationUrl)
    {
        if (state.SignIn?.FlowId != flowId)
        {
            return state;
        }

        return state with
        {
            Clipboard = userCode,
            SignIn = state.SignIn with
            {
                Message = "Open the link and enter the code, which is on the clipboard.",
                UserCode = userCode,
                VerificationUrl = verificationUrl
            }
        };
    }

    /// <summary>
    /// Puts the code back on the clipboard, for the user whose clipboard was overwritten between
    /// the code arriving and the provider's page asking for it.
    /// </summary>
    public static SessionState CopyUserCode(SessionState state)
    {
        if (state.SignIn?.UserCode is { } code)
        {
            return SetStatus(state with { Clipboard = code }, "Copied the code");
        }

        return state;
    }

    public static SessionState SignInCompleted(SessionState state, Guid flowId, string? userName)
    {
        if (state.SignIn?.FlowId != flowId ||
            state.Form is not ConnectionFormState form)
        {
            return state;
        }

        var message = userName is null ? "Signed in." : $"Signed in as {userName}.";
        return state with
        {
            Page = form.Page,
            SignIn = null,
            Form = form with { SignedIn = true, Message = message, Error = null }
        };
    }

    public static SessionState SignInFailed(SessionState state, Guid flowId, string error)
    {
        if (state.SignIn?.FlowId != flowId ||
            state.Form is not ConnectionFormState form)
        {
            return state;
        }

        return state with
        {
            Page = form.Page,
            SignIn = null,
            Form = form with { Error = new(error, FormFields.Auth) }
        };
    }

    public static SessionState CancelSignIn(SessionState state)
    {
        if (state.SignIn is null)
        {
            return state;
        }

        return state with { Page = state.Form?.Page ?? Page.Builds, SignIn = null };
    }

    // Polling

    public static SessionState SetHealth(SessionState state, string connectionId, ConnectionHealth health, string? error = null, DateTimeOffset? retryAfter = null) =>
        UpdateConnection(state, connectionId, _ => _ with { Health = health, Error = error, RetryAfter = retryAfter, Progress = null });

    public static SessionState SetProgress(SessionState state, string connectionId, PollProgress progress) =>
        UpdateConnection(state, connectionId, _ => _.Health == ConnectionHealth.Polling ? _ with { Progress = progress } : _);

    /// <summary>
    /// The result of one poll: that connection's builds are replaced wholesale. Everything else
    /// is untouched, so a slow connection never hides a fast one's news.
    /// </summary>
    public static SessionState ApplyPoll(SessionState state, string connectionId, ImmutableArray<Pipeline> pipelines, ImmutableArray<Build> builds, DateTimeOffset now)
    {
        var next = UpdateConnection(
            state,
            connectionId,
            _ => _ with { Health = ConnectionHealth.Ok, Error = null, RetryAfter = null, LastPolled = now, Pipelines = pipelines, Progress = null });
        var previous = state.Builds.Where(_ => _.ConnectionId == connectionId).ToImmutableArray();
        var notification = state.Notification;
        if (state.Settings.NotifyOnFailure)
        {
            notification = FailureDetector.Describe(FailureDetector.NewFailures(previous, builds)) ?? notification;
        }

        return Follow(
            state,
            next with
            {
                Builds =
                [
                    ..next.Builds.Where(_ => _.ConnectionId != connectionId),
                    ..builds
                ],
                Notification = notification
            },
            now);
    }

    /// <summary>
    /// The result of one scheduled cycle, which fetched only the groups that were due. The fetched
    /// pipelines' builds are replaced, builds of pipelines no longer discovered go, and the rest
    /// stay. Replacing wholesale, as <see cref="ApplyPoll"/> does, would blank every row the cycle
    /// did not fetch.
    /// <para>
    /// A pipeline fetched for the first time is not news. When the request quota defers groups
    /// after a start, their first fetch would otherwise announce every red pipeline at once.
    /// </para>
    /// <para>
    /// A connection that can only watch has retry and cancel taken off every build it holds, those
    /// kept as well as those fetched, so the answer applies with the cycle that brought it rather
    /// than as each group comes due.
    /// </para>
    /// </summary>
    public static SessionState ApplyFetch(SessionState state, string connectionId, FetchOutcome outcome, DateTimeOffset now)
    {
        var discovered = outcome.Pipelines.Select(_ => _.Id).ToHashSet();
        var previous = state.Builds.Where(_ => _.ConnectionId == connectionId).ToImmutableArray();
        // Dropped here rather than left out of most requests: a service that filters on when a build was
        // created or queued would also leave out one from before the cutoff that is still running.
        // Also covers a response served from an ETag cached before the cutoff moved on.
        var cutoff = HistoryCutoff.Of(now, state.Settings.HistoryDays);
        var arrived = outcome.Builds
            .Where(_ => HistoryCutoff.Keeps(_, cutoff))
            .Select(_ => Offered(_, outcome.Access))
            .ToImmutableArray();
        var next = UpdateConnection(
            state,
            connectionId,
            _ => _ with
            {
                Health = outcome.Health,
                Error = outcome.Error,
                RetryAfter = outcome.RetryAfter,
                LastPolled = outcome.Fetched.Count > 0 ? now : _.LastPolled,
                Pipelines = outcome.Pipelines,
                Progress = null,
                Access = outcome.Access
            });
        var notification = state.Notification;
        if (state.Settings.NotifyOnFailure)
        {
            var news = arrived.Where(_ => !outcome.FirstFetch.Contains(_.PipelineId)).ToImmutableArray();
            notification = FailureDetector.Describe(FailureDetector.NewFailures(previous, news)) ?? notification;
        }

        return Follow(
            state,
            next with
            {
                Builds =
                [
                    ..next.Builds.Where(_ => _.ConnectionId != connectionId),
                    ..previous
                        .Where(_ => discovered.Contains(_.PipelineId) && !outcome.Fetched.Contains(_.PipelineId) && HistoryCutoff.Keeps(_, cutoff))
                        .Select(_ => Offered(_, outcome.Access)),
                    ..arrived
                ],
                Notification = notification
            },
            now);
    }

    /// <summary>
    /// How long after a poll changes what a position shows a click on it is still taken as aimed
    /// at what was there: longer than it takes to see a row move and aim again, short enough that a
    /// second click after reading the status line acts.
    /// </summary>
    public static readonly TimeSpan MoveGrace = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Records which visible positions a poll changed. Only a poll's changes count: a group the user
    /// opened, or a filter they typed, moves rows under a pointer they know is moving them. A change
    /// still inside its grace joins the new one, where the list has not scrolled since, so a second
    /// connection's poll landing just after the first does not clear what the first moved.
    /// </summary>
    static SessionState Moved(SessionState before, ImmutableArray<Row> previous, SessionState after, ImmutableArray<Row> rows, DateTimeOffset now)
    {
        var positions = ImmutableHashSet.CreateBuilder<int>();
        if (after.Moved is { } earlier &&
            now - earlier.At < MoveGrace &&
            earlier.ScrollTop == after.ScrollTop)
        {
            positions.UnionWith(earlier.Positions);
        }

        var body = BodyRows(after);
        for (var position = 0; position < body; position++)
        {
            if (Occupant(previous, before.ScrollTop + position) != Occupant(rows, after.ScrollTop + position))
            {
                positions.Add(position);
            }
        }

        if (positions.Count == 0)
        {
            return after;
        }

        return after with { Moved = new(now, after.ScrollTop, positions.ToImmutable()) };
    }

    /// <summary>
    /// What a position shows, as far as a click on it goes: which row, and for a build its status,
    /// since a build that finishes where it stood swaps Cancel for Retry under the pointer. Null
    /// past the end of the rows.
    /// </summary>
    static (RowIdentity Row, BuildStatus? Status)? Occupant(ImmutableArray<Row> rows, int index)
    {
        if (index < 0 ||
            index >= rows.Length)
        {
            return null;
        }

        return (Identity(rows[index]), rows[index].Build?.Status);
    }

    /// <summary>
    /// Whether a click at <paramref name="at"/> on the visible <paramref name="position"/> came
    /// within <see cref="MoveGrace"/> of a poll changing what it shows. A click from before the
    /// poll, as an unstamped one in a test reads, is not.
    /// </summary>
    public static bool JustMoved(SessionState state, int position, DateTimeOffset at) =>
        state.Moved is { } moved &&
        at >= moved.At &&
        at - moved.At < MoveGrace &&
        moved.ScrollTop == state.ScrollTop &&
        moved.Positions.Contains(position);

    static Build Offered(Build build, BuildAccess access)
    {
        if (access == BuildAccess.Watch)
        {
            return build.WatchOnly();
        }

        return build;
    }

    public static SessionState Notify(SessionState state, Notification notification) =>
        state with { Notification = notification };

    public static SessionState ClearNotification(SessionState state) =>
        state with { Notification = null };

    public static SessionState ApplyMedians(SessionState state, ImmutableDictionary<string, TimeSpan> medians) =>
        state with { Medians = medians };

    public static SessionState ApplyLocalRepos(SessionState state, ImmutableDictionary<string, string> repos) =>
        state with { LocalRepos = repos };

    static SessionState UpdateConnection(SessionState state, string connectionId, Func<ConnectionState, ConnectionState> change)
    {
        var index = -1;
        for (var candidate = 0; candidate < state.Connections.Length; candidate++)
        {
            if (state.Connections[candidate].Connection.Id == connectionId)
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            return state;
        }

        return state with { Connections = state.Connections.SetItem(index, change(state.Connections[index])) };
    }

    // Window

    public static SessionState SetStatus(SessionState state, string status)
    {
        if (state.Status == status)
        {
            return state;
        }

        return state with { Status = status };
    }

    /// <summary>
    /// Text fetched in the background, waiting for the loop to put it on the clipboard.
    /// </summary>
    public static SessionState Copy(SessionState state, string text, string status) =>
        state with { Clipboard = text, Status = status };

    /// <summary>
    /// Clears only the text the loop copied, so a second log that arrived meanwhile keeps its turn.
    /// </summary>
    public static SessionState Copied(SessionState state, string text)
    {
        if (ReferenceEquals(state.Clipboard, text))
        {
            return state with { Clipboard = null };
        }

        return state;
    }

    /// <summary>
    /// What the status says when the desktop would not take the text. It asks for the click again
    /// because that is the whole remedy: whatever is holding the clipboard lets go of it in a
    /// moment, and every button that copies can be pressed twice.
    /// </summary>
    public const string ClipboardBusy = "Could not copy: another app is holding the clipboard. Try again.";

    /// <summary>
    /// Gives up on text the window would not take, after <see cref="ClipboardPump"/> has tried.
    /// Clears it for the same reason <see cref="Copied"/> does, and replaces the status that said
    /// it had been copied: a stale clipboard under a status line claiming otherwise is how this was
    /// invisible in the first place.
    /// </summary>
    public static SessionState CopyFailed(SessionState state, string text)
    {
        if (ReferenceEquals(state.Clipboard, text))
        {
            return state with { Clipboard = null, Status = ClipboardBusy };
        }

        return state;
    }

    public static SessionState Hide(SessionState state) =>
        state with { Hidden = true, Menu = null };

    public static SessionState Show(SessionState state) =>
        state with { Hidden = false };

    public static SessionState Quit(SessionState state) =>
        state with { Exit = true };

    static string Flag(bool value)
    {
        if (value)
        {
            return "true";
        }

        return "false";
    }
}
