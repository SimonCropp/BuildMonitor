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
    /// Keeps the scroll top and the selection inside the rows that exist, which changes with
    /// every poll and every fold.
    /// </summary>
    static SessionState Clamp(SessionState state)
    {
        var total = RowProjection.Rows(state).Length;
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

    // Folding

    public static SessionState ToggleGroup(SessionState state, string connectionId)
    {
        var unfolded = state.FoldedGroups.Remove(connectionId);
        var folded = ReferenceEquals(unfolded, state.FoldedGroups)
            ? state.FoldedGroups.Add(connectionId)
            : unfolded;
        return Clamp(state with { FoldedGroups = folded });
    }

    /// <summary>
    /// Expands a project's shared row into its builds, or gathers them back. The selection follows
    /// the project, since the rows under the cursor change either way.
    /// </summary>
    public static SessionState ToggleProject(SessionState state, string projectKey)
    {
        var removed = state.ExpandedProjects.Remove(projectKey);
        var expanded = ReferenceEquals(removed, state.ExpandedProjects)
            ? state.ExpandedProjects.Add(projectKey)
            : removed;
        var next = state with { ExpandedProjects = expanded };
        var rows = RowProjection.Rows(next);
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].Builds.Any(_ => _.Status == BuildStatus.Succeeded && _.ProjectKey == projectKey))
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
        if (target.Kind == RowKind.Project)
        {
            items.Add(new("Expand", CommandKind.ToggleProject));
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
            if (build.CanRetry)
            {
                items.Add(new("Retry", CommandKind.Retry));
            }

            if (build.CanCancel)
            {
                items.Add(new("Cancel build", CommandKind.Cancel));
            }

            if (build.Status == BuildStatus.Succeeded &&
                state.ExpandedProjects.Contains(build.ProjectKey) &&
                RowProjection.Collapsible(RowProjection.Builds(state, build.ConnectionId)).Contains(build.ProjectKey))
            {
                items.Add(new($"Collapse {build.ShortRepoName()}", CommandKind.ToggleProject));
            }

            items.Add(new("Refresh", CommandKind.Refresh));
            items.Add(new($"Exclude {build.PipelineName}", CommandKind.ExcludePipeline));
        }
        else
        {
            items.Add(new(target.Folded ? "Unfold" : "Fold", CommandKind.ToggleGroup));
            items.Add(new("Refresh", CommandKind.Refresh));
            items.Add(new("Edit connection", CommandKind.EditConnection));
        }

        return SelectRow(state, row) with { Menu = new(row, items.ToImmutable()) };
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
        values[FormFields.NotifyOnFailure] = Flag(settings.NotifyOnFailure);
        values[FormFields.Theme] = settings.Theme.ToString();
        values[FormFields.PollInterval] = settings.PollIntervalSeconds.ToString();
        values[FormFields.RunningPollInterval] = settings.RunningPollIntervalSeconds.ToString();
        values[FormFields.Port] = settings.Port.ToString();
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
        state.Form is null ? state : state with { Form = state.Form with { Error = error } };

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
        return Clamp(state with
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

        return Clamp(next with
        {
            Builds =
            [
                ..next.Builds.Where(_ => _.ConnectionId != connectionId),
                ..builds
            ],
            Notification = notification
        });
    }

    public static SessionState ClearNotification(SessionState state) =>
        state with { Notification = null };

    public static SessionState ApplyMedians(SessionState state, ImmutableDictionary<string, TimeSpan> medians) =>
        state with { Medians = medians };

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

    public static SessionState Hide(SessionState state) =>
        state with { Hidden = true, Menu = null };

    public static SessionState Show(SessionState state) =>
        state with { Hidden = false };

    public static SessionState Quit(SessionState state) =>
        state with { Exit = true };

    static string Flag(bool value) =>
        value ? "true" : "false";
}
