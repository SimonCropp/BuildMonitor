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
    static SessionState Follow(SessionState before, SessionState after)
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
            return followed with { Menu = null };
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
    public static SessionState Search(SessionState state, string text) =>
        state.Search == text
            ? state
            : EnsureVisible(Follow(state, state with { Search = text }));

    // Groups

    /// <summary>
    /// Opens a group or closes it. The selection moves to the group's own row, since the rows
    /// under the cursor change either way and a member row would vanish on closing.
    /// </summary>
    public static SessionState ToggleGroup(SessionState state, GroupKey key)
    {
        var removed = state.ToggledGroups.Remove(key.Id);
        var toggled = ReferenceEquals(removed, state.ToggledGroups)
            ? state.ToggledGroups.Add(key.Id)
            : removed;
        var next = state with { ToggledGroups = toggled };
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
            items.Add(new("Refresh", CommandKind.Refresh));
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

            if (build.Retryable())
            {
                items.Add(new("Retry", CommandKind.Retry));
            }

            if (build.CanCancel)
            {
                items.Add(new("Cancel build", CommandKind.Cancel));
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
            items.Add(new($"Exclude {build.PipelineName} {PipelineNoun(state, build)}", CommandKind.ExcludePipeline));
        }

        return SelectRow(state, row) with { Menu = new(row, items.ToImmutable()) };
    }

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

        var items = RowChips.Of(build, state.LocalRepos)
            .Where(_ => _.Kind >= from)
            .Select(_ => new MenuItem(_.Label, RowChips.Command(_.Kind)))
            .ToImmutableArray();
        if (items.Length == 0)
        {
            return state;
        }

        return SelectRow(state, row) with { Menu = new(row, items, Overflow: true) };
    }

    public static SessionState CloseMenu(SessionState state) =>
        state.Menu is null ? state : state with { Menu = null };

    public static (SessionState State, CommandKind Command) ChooseMenuItem(SessionState state, int index)
    {
        if (state.Menu is null ||
            index < 0 ||
            index >= state.Menu.Items.Length)
        {
            return (CloseMenu(state), CommandKind.None);
        }

        return (CloseMenu(state), state.Menu.Items[index].Command);
    }

    // Pages

    public static SessionState OpenBuilds(SessionState state) =>
        state with { Page = Page.Builds, Form = null, SignIn = null, Menu = null };

    public static SessionState OpenOptions(SessionState state)
    {
        var settings = state.Settings;
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        values[FormFields.RunAtStartup] = Flag(settings.RunAtStartup);
        values[FormFields.ShowWindowAtStart] = Flag(settings.ShowWindowAtStart);
        values[FormFields.ShowOtherBranches] = Flag(settings.ShowOtherBranches);
        values[FormFields.ShowForks] = Flag(settings.ShowForksAndCollaborations);
        values[FormFields.NotifyOnFailure] = Flag(settings.NotifyOnFailure);
        values[FormFields.Theme] = settings.Theme.ToString();
        values[FormFields.PollInterval] = settings.PollIntervalSeconds.ToString();
        values[FormFields.RunningPollInterval] = settings.RunningPollIntervalSeconds.ToString();
        values[FormFields.HistoryDays] = settings.HistoryDays.ToString();
        values[FormFields.Port] = settings.Port.ToString();
        values[FormFields.CodeDirectory] = settings.CodeDirectory;
        return state with
        {
            Page = Page.Options,
            Menu = null,
            Form = new(Page.Options, values.ToImmutable(), settings.Filters, null, null, null, null, false)
        };
    }

    public static SessionState OpenFilters(SessionState state)
    {
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        values[FormFields.FilterKind] = nameof(FilterKind.Prefix);
        values[FormFields.FilterTarget] = nameof(FilterTarget.Pipeline);
        values[FormFields.FilterText] = "";
        return state with
        {
            Page = Page.Filters,
            Menu = null,
            Form = new(Page.Filters, values.ToImmutable(), state.Settings.Filters, null, null, null, null, false)
        };
    }

    public static SessionState OpenConnectionEditor(SessionState state, string? connectionId, string draftId)
    {
        var existing = connectionId is null ? null : state.Connection(connectionId)?.Connection;
        var descriptor = existing is null ? ProviderDescriptors.All[0] : ProviderDescriptors.Get(existing.ProviderId);
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        values[FormFields.Provider] = descriptor.Name;
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

        return state with
        {
            Page = Page.Connection,
            Menu = null,
            Form = new(
                Page.Connection,
                values.ToImmutable(),
                state.Settings.Filters,
                null,
                existing?.Id,
                existing is null ? draftId : null,
                null,
                // An existing connection already has its secret stored.
                existing is not null)
        };
    }

    public static SessionState FieldChanged(SessionState state, string id, string value)
    {
        if (state.Form is null)
        {
            return state;
        }

        var form = state.Form.With(id, value);
        // Switching provider re-derives the dependent fields: server, auth method and scopes.
        if (id == FormFields.Provider &&
            state.Form.Page == Page.Connection &&
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

    public static SessionState SetFormError(SessionState state, string error) =>
        // Clears the message too, or a failed test leaves "Testing..." above its error.
        state.Form is null ? state : state with { Form = state.Form with { Error = error, Message = null } };

    public static SessionState SetFormMessage(SessionState state, string message) =>
        state.Form is null ? state : state with { Form = state.Form with { Message = message, Error = null } };

    // Filters page

    public static SessionState AddFilter(SessionState state)
    {
        if (state.Form is not { Page: Page.Filters } form)
        {
            return state;
        }

        var text = form.Value(FormFields.FilterText).Trim();
        if (text.Length == 0)
        {
            return SetFormError(state, "Enter the text to match.");
        }

        if (!Enum.TryParse<FilterKind>(form.Value(FormFields.FilterKind), out var kind) ||
            !Enum.TryParse<FilterTarget>(form.Value(FormFields.FilterTarget), out var target))
        {
            return SetFormError(state, "Choose a kind and a target.");
        }

        var filter = new Filter(kind, target, text);
        if (form.Filters.Contains(filter))
        {
            return SetFormError(state, "That filter already exists.");
        }

        return state with
        {
            Form = form
                .With(FormFields.FilterText, "")
                with
                {
                    Filters = form.Filters.Add(filter),
                    Error = null
                }
        };
    }

    public static SessionState RemoveFilter(SessionState state, int index)
    {
        if (state.Form is not { Page: Page.Filters } form ||
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
    /// The context menu's "Exclude": an exact filter on the pipeline name, applied at once.
    /// </summary>
    public static SessionState ExcludePipeline(SessionState state, Build build)
    {
        var filter = new Filter(FilterKind.Exact, FilterTarget.Pipeline, build.PipelineName);
        if (state.Settings.Filters.Contains(filter))
        {
            return state;
        }

        return ApplySettings(state, state.Settings with { Filters = state.Settings.Filters.Add(filter) });
    }

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
                return current is null ? ConnectionState.Start(_) : current with { Connection = _ };
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

    public static SessionState UpsertConnection(SessionState state, Connection connection)
    {
        var existing = state.Settings.Connections.FirstOrDefault(_ => _.Id == connection.Id);
        var connections = existing is null
            ? state.Settings.Connections.Add(connection)
            : state.Settings.Connections.Replace(existing, connection);
        return ApplySettings(state, state.Settings with { Connections = connections });
    }

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

    public static SessionState SignInProgress(SessionState state, Guid flowId, string userCode, string verificationUrl)
    {
        if (state.SignIn?.FlowId != flowId)
        {
            return state;
        }

        return state with
        {
            SignIn = state.SignIn with
            {
                Message = "Open the link and enter the code.",
                UserCode = userCode,
                VerificationUrl = verificationUrl
            }
        };
    }

    public static SessionState SignInCompleted(SessionState state, Guid flowId, string? userName)
    {
        if (state.SignIn?.FlowId != flowId ||
            state.Form is null)
        {
            return state;
        }

        var message = userName is null ? "Signed in." : $"Signed in as {userName}.";
        return state with
        {
            Page = Page.Connection,
            SignIn = null,
            Form = state.Form with { SignedIn = true, Message = message, Error = null }
        };
    }

    public static SessionState SignInFailed(SessionState state, Guid flowId, string error)
    {
        if (state.SignIn?.FlowId != flowId ||
            state.Form is null)
        {
            return state;
        }

        return state with
        {
            Page = Page.Connection,
            SignIn = null,
            Form = state.Form with { Error = error }
        };
    }

    public static SessionState CancelSignIn(SessionState state) =>
        state.SignIn is null
            ? state
            : state with { Page = Page.Connection, SignIn = null };

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

        return Follow(state, next with
        {
            Builds =
            [
                ..next.Builds.Where(_ => _.ConnectionId != connectionId),
                ..builds
            ],
            Notification = notification
        });
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

        return Follow(state, next with
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
        });
    }

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

    public static SessionState SetStatus(SessionState state, string status) =>
        state.Status == status ? state : state with { Status = status };

    /// <summary>
    /// Text fetched in the background, waiting for the loop to put it on the clipboard.
    /// </summary>
    public static SessionState Copy(SessionState state, string text, string status) =>
        state with { Clipboard = text, Status = status };

    /// <summary>
    /// Clears only the text the loop copied, so a second log that arrived meanwhile keeps its turn.
    /// </summary>
    public static SessionState Copied(SessionState state, string text) =>
        ReferenceEquals(state.Clipboard, text) ? state with { Clipboard = null } : state;

    public static SessionState Hide(SessionState state) =>
        state with { Hidden = true, Menu = null };

    public static SessionState Show(SessionState state) =>
        state with { Hidden = false };

    public static SessionState Quit(SessionState state) =>
        state with { Exit = true };

    static string Flag(bool value) =>
        value ? "true" : "false";
}
