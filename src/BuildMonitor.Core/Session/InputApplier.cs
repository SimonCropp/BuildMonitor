/// <summary>
/// Turns one frame's <see cref="MonitorInput"/> into the next state, asking
/// <see cref="MonitorActions"/> for whatever has to happen in the world. The one place a click
/// is interpreted, for every head.
/// </summary>
static class InputApplier
{
    public static SessionState Apply(SessionState state, MonitorInput input, MonitorActions actions, IMonitorWindow window)
    {
        if (input is {Columns: > 0, Rows: > 0} &&
            (input.Columns != state.Columns || input.Rows != state.Rows))
        {
            state = MonitorSession.Resize(state, input.Columns, input.Rows);
        }

        if (input.Any)
        {
            // The last message has been seen.
            state = MonitorSession.SetStatus(state, "");
        }

        if (input.ScrollDelta != 0)
        {
            state = MonitorSession.Scroll(state, input.ScrollDelta);
        }

        if (input.ScrollTo >= 0)
        {
            state = MonitorSession.ScrollTo(state, input.ScrollTo);
        }

        if (input.FieldChanges is { Count: > 0 } changes)
        {
            foreach (var change in changes)
            {
                state = MonitorSession.FieldChanged(state, change.Id, change.Value);
            }
        }

        if (input.MenuClosed)
        {
            state = MonitorSession.CloseMenu(state);
        }

        if (input.ClickedRow >= 0)
        {
            var clicked = state.ScrollTop + input.ClickedRow;
            state = MonitorSession.SelectRow(MonitorSession.CloseMenu(state), clicked);
            // A group row draws an arrow and has nothing to open, so a click on it opens or closes
            // it. Selecting it alone looked like the click did nothing. A head sends a double click
            // on a group as one click, or it would open and close again.
            if (state.SelectedRow == clicked &&
                MonitorSession.SelectedRow(state) is { Kind: RowKind.Group, Group: { } group })
            {
                state = MonitorSession.ToggleGroup(state, group);
            }
        }

        if (input.ClickedChipRow >= 0)
        {
            state = ClickChip(state, state.ScrollTop + input.ClickedChipRow, input.ClickedChip, actions, window);
        }

        if (input.ClickedOverflowRow >= 0)
        {
            state = MonitorSession.OpenOverflow(state, state.ScrollTop + input.ClickedOverflowRow, input.OverflowFrom);
        }

        if (input.RightClickedRow >= 0)
        {
            state = MonitorSession.OpenMenu(state, state.ScrollTop + input.RightClickedRow);
        }

        if (input.ClickedMenuItem >= 0)
        {
            var (chosen, command) = MonitorSession.ChooseMenuItem(state, input.ClickedMenuItem);
            state = Execute(chosen, command, null, actions, window);
        }

        if (input.ClickedButton >= 0)
        {
            var buttons = ScreenBuilder.Buttons(state);
            if (input.ClickedButton < buttons.Count &&
                buttons[input.ClickedButton].Enabled)
            {
                state = Execute(state, buttons[input.ClickedButton].Command, null, actions, window);
            }
        }

        if (input.ClickedField is { } field)
        {
            state = ClickField(state, field, actions);
        }

        // After the clicks, which index the rows that were on screen, and before the keys, so an
        // Enter pressed as the text changed opens what the filter left selected.
        if (input.Search is { } search)
        {
            state = MonitorSession.Search(state, search);
        }

        if (input.Key != CommandKind.None)
        {
            state = Execute(state, input.Key, null, actions, window);
        }

        if (input.TrayIconClicked)
        {
            state = Show(state, window);
        }

        if (input.TrayItem is { } item)
        {
            state = TrayItem(state, item, actions, window);
        }

        if (input.CloseRequested)
        {
            state = Execute(state, CommandKind.Hide, null, actions, window);
        }

        return state;
    }

    /// <summary>
    /// A chip acts on the selected build, so its row is selected first. A row that is gone, or is a
    /// group now, takes nothing: clamped, the selection would land on another build and act on it.
    /// </summary>
    static SessionState ClickChip(SessionState state, int row, ChipKind chip, MonitorActions actions, IMonitorWindow? window)
    {
        var rows = RowProjection.Rows(state);
        if (row < 0 ||
            row >= rows.Length ||
            rows[row].Build is null)
        {
            return state;
        }

        return Execute(MonitorSession.SelectRow(state, row), RowChips.Command(chip), null, actions, window);
    }

    static SessionState ClickField(SessionState state, string id, MonitorActions actions)
    {
        if (id.StartsWith(FormFields.ConnectionPrefix, StringComparison.Ordinal))
        {
            return Execute(state, CommandKind.EditConnection, id[FormFields.ConnectionPrefix.Length..], actions, null);
        }

        if (id.StartsWith(FormFields.FilterPrefix, StringComparison.Ordinal))
        {
            return Execute(state, CommandKind.RemoveFilter, id[FormFields.FilterPrefix.Length..], actions, null);
        }

        switch (id)
        {
            case FormFields.AddConnection:
                return Execute(state, CommandKind.AddConnection, null, actions, null);
            case FormFields.AddFilter:
                return Execute(state, CommandKind.AddFilter, null, actions, null);
            case FormFields.OpenLogs:
                return Execute(state, CommandKind.OpenLogs, null, actions, null);
            case FormFields.RaiseIssue:
                return Execute(state, CommandKind.RaiseIssue, null, actions, null);
            case FormFields.Update:
                return Execute(state, CommandKind.Update, null, actions, null);
            case FormFields.Documentation:
                actions.OpenUrl("https://github.com/SimonCropp/BuildMonitor");
                return state;
            case FormFields.TokenHelp when state.Form is not null:
                actions.OpenUrl(ConnectionDraft.Descriptor(state.Form).TokenHelpUrl);
                return state;
            case FormFields.VerificationUrl when state.SignIn?.VerificationUrl is { } url:
                actions.OpenUrl(url);
                return state;
            default:
                return state;
        }
    }

    static SessionState TrayItem(SessionState state, string id, MonitorActions actions, IMonitorWindow window) =>
        id switch
        {
            TrayMenu.Open => Show(state, window),
            TrayMenu.Refresh => Execute(state, CommandKind.Refresh, null, actions, window),
            TrayMenu.Options => Show(Execute(state, CommandKind.OpenOptions, null, actions, window), window),
            TrayMenu.Filters => Show(Execute(state, CommandKind.OpenFilters, null, actions, window), window),
            TrayMenu.Logs => Execute(state, CommandKind.OpenLogs, null, actions, window),
            TrayMenu.Issue => Execute(state, CommandKind.RaiseIssue, null, actions, window),
            TrayMenu.Update => Execute(state, CommandKind.Update, null, actions, window),
            TrayMenu.Exit => Execute(state, CommandKind.Quit, null, actions, window),
            _ => state
        };

    static SessionState Show(SessionState state, IMonitorWindow? window)
    {
        window?.SetHidden(false);
        window?.Focus();
        return MonitorSession.Show(state);
    }

    /// <summary>
    /// One command, from whichever surface asked for it.
    /// </summary>
    /// <param name="target">A connection id or a filter index, for the commands that name one.</param>
    public static SessionState Execute(SessionState state, CommandKind command, string? target, MonitorActions actions, IMonitorWindow? window)
    {
        switch (command)
        {
            case CommandKind.None:
                return state;
            case CommandKind.ScrollUp:
                return MonitorSession.Scroll(state, -1);
            case CommandKind.ScrollDown:
                return MonitorSession.Scroll(state, 1);
            case CommandKind.PageUp:
                return MonitorSession.PageUp(state);
            case CommandKind.PageDown:
                return MonitorSession.PageDown(state);
            case CommandKind.ScrollHome:
                return MonitorSession.ScrollHome(state);
            case CommandKind.ScrollEnd:
                return MonitorSession.ScrollEnd(state);
            case CommandKind.NextRow:
                return MonitorSession.NextRow(state);
            case CommandKind.PreviousRow:
                return MonitorSession.PreviousRow(state);
            case CommandKind.OpenBuild:
            case CommandKind.OpenBranch:
            case CommandKind.OpenPullRequest:
            case CommandKind.OpenProject:
            {
                // A group has no one build to open, so Enter or a double click opens or closes it.
                if (command == CommandKind.OpenBuild &&
                    MonitorSession.SelectedRow(state) is { Kind: RowKind.Group, Group: { } group })
                {
                    return MonitorSession.ToggleGroup(state, group);
                }

                if (MonitorSession.SelectedBuild(state) is not { } build)
                {
                    return state;
                }

                var url = command switch
                {
                    CommandKind.OpenBranch => build.BranchUrl,
                    CommandKind.OpenPullRequest => build.PullRequestUrl,
                    CommandKind.OpenProject => build.ProjectUrl,
                    _ => build.BuildUrl
                };
                if (url is not null)
                {
                    actions.OpenUrl(url);
                }

                return state;
            }
            case CommandKind.CopyBuildUrl:
                if (MonitorSession.SelectedBuild(state) is { } copied)
                {
                    window?.SetClipboard(copied.BuildUrl);
                    return MonitorSession.SetStatus(state, "Copied build URL");
                }

                return state;
            case CommandKind.CopyLog:
                return MonitorSession.SelectedBuild(state) is { } logged ? CopyLog(state, logged, actions) : state;
            case CommandKind.Retry:
                return MonitorSession.SelectedBuild(state) is { } retry ? Retry(state, retry, actions) : state;
            case CommandKind.Cancel:
                return MonitorSession.SelectedBuild(state) is { } cancel ? Cancel(state, cancel, actions) : state;
            case CommandKind.Refresh:
                actions.Refresh(null);
                return MonitorSession.SetStatus(state, "Refreshing");
            case CommandKind.ToggleGroup:
                return MonitorSession.SelectedRow(state)?.Group is { } toggled
                    ? MonitorSession.ToggleGroup(state, toggled)
                    : state;
            case CommandKind.ExcludePipeline:
            {
                if (MonitorSession.SelectedBuild(state) is not { } build)
                {
                    return state;
                }

                state = MonitorSession.ExcludePipeline(state, build);
                actions.SaveSettings(state.Settings);
                return MonitorSession.SetStatus(state, $"Excluded {build.PipelineName}");
            }
            case CommandKind.OpenBuilds:
                return MonitorSession.OpenBuilds(state);
            case CommandKind.OpenOptions:
                return MonitorSession.OpenOptions(state);
            case CommandKind.OpenFilters:
                return MonitorSession.OpenFilters(state);
            case CommandKind.AddConnection:
                return MonitorSession.OpenConnectionEditor(state, null, Guid.NewGuid().ToString("N"));
            case CommandKind.EditConnection:
            {
                var id = target ?? MonitorSession.SelectedRow(state)?.Connection?.Connection.Id;
                return id is null ? state : MonitorSession.OpenConnectionEditor(state, id, Guid.NewGuid().ToString("N"));
            }
            case CommandKind.RemoveConnection:
            {
                if (state.Form?.EditingConnectionId is not { } id)
                {
                    return state;
                }

                state = MonitorSession.OpenBuilds(MonitorSession.RemoveConnection(state, id));
                actions.DeleteSecret(SecretKeys.Token(id));
                actions.DeleteSecret(SecretKeys.Refresh(id));
                actions.SaveSettings(state.Settings);
                return MonitorSession.SetStatus(state, "Connection removed");
            }
            case CommandKind.AddFilter:
                return MonitorSession.AddFilter(state);
            case CommandKind.RemoveFilter:
                return int.TryParse(target, out var index) ? MonitorSession.RemoveFilter(state, index) : state;
            case CommandKind.SignIn:
            {
                if (state.Form is not { Page: Page.Connection } form)
                {
                    return state;
                }

                var method = ConnectionDraft.Method(form);
                if (method == AuthMethod.Token)
                {
                    return state;
                }

                var connection = ConnectionDraft.Build(form);
                var flowId = Guid.NewGuid();
                state = MonitorSession.BeginSignIn(state, connection, method, flowId);
                actions.SignIn(connection, method, flowId);
                return state;
            }
            case CommandKind.CancelSignIn:
                if (state.SignIn is { } signIn)
                {
                    actions.CancelSignIn(signIn.FlowId);
                }

                return MonitorSession.CancelSignIn(state);
            case CommandKind.TestConnection:
            {
                if (state.Form is not { Page: Page.Connection } form)
                {
                    return state;
                }

                actions.Test(ConnectionDraft.Build(form), ConnectionDraft.Token(form));
                return MonitorSession.SetFormMessage(state, "Testing...");
            }
            case CommandKind.Save:
                return Save(state, actions);
            case CommandKind.CancelForm:
            {
                // A new connection that signed in before being abandoned leaves a token behind.
                if (state.Form is { Page: Page.Connection, EditingConnectionId: null, SignedIn: true, DraftConnectionId: { } draft })
                {
                    actions.DeleteSecret(SecretKeys.Token(draft));
                    actions.DeleteSecret(SecretKeys.Refresh(draft));
                }

                return MonitorSession.OpenBuilds(state);
            }
            case CommandKind.OpenLogs:
                actions.OpenLogs();
                return state;
            case CommandKind.RaiseIssue:
                actions.RaiseIssue();
                return state;
            case CommandKind.Update:
                actions.Update();
                return state;
            case CommandKind.Hide:
                window?.SetHidden(true);
                return MonitorSession.Hide(state);
            case CommandKind.Quit:
                return MonitorSession.Quit(state);
            default:
                return state;
        }
    }

    static SessionState Retry(SessionState state, Build build, MonitorActions actions)
    {
        if (!build.Retryable())
        {
            return state;
        }

        actions.Retry(build);
        return MonitorSession.SetStatus(state, $"Retrying {build.PipelineName} {build.RunNumberLabel()}".TrimEnd());
    }

    static SessionState Cancel(SessionState state, Build build, MonitorActions actions)
    {
        if (!build.CanCancel)
        {
            return state;
        }

        actions.Cancel(build);
        return MonitorSession.SetStatus(state, $"Cancelling {build.PipelineName} {build.RunNumberLabel()}".TrimEnd());
    }

    static SessionState CopyLog(SessionState state, Build build, MonitorActions actions)
    {
        if (!build.LogCopyable())
        {
            return state;
        }

        actions.CopyLog(build);
        return MonitorSession.SetStatus(state, $"Fetching the log of {build.PipelineName} {build.RunNumberLabel()}".TrimEnd());
    }

    static SessionState Save(SessionState state, MonitorActions actions)
    {
        if (state.Form is not { } form)
        {
            return state;
        }

        switch (form.Page)
        {
            case Page.Options:
            {
                if (!OptionsDraft.TryBuild(form, state.Settings, out var settings, out var error))
                {
                    return MonitorSession.SetFormError(state, error);
                }

                if (settings.RunAtStartup != state.Settings.RunAtStartup)
                {
                    actions.SetRunAtLogin(settings.RunAtStartup);
                }

                state = MonitorSession.ApplySettings(state, settings);
                actions.SaveSettings(settings);
                return MonitorSession.SetStatus(MonitorSession.OpenBuilds(state), "Options saved");
            }
            case Page.Filters:
            {
                state = MonitorSession.ApplySettings(state, state.Settings with { Filters = form.Filters });
                actions.SaveSettings(state.Settings);
                return MonitorSession.SetStatus(MonitorSession.OpenBuilds(state), "Filters saved");
            }
            case Page.Connection:
            {
                if (ConnectionDraft.Validate(form) is { } error)
                {
                    return MonitorSession.SetFormError(state, error);
                }

                var connection = ConnectionDraft.Build(form);
                if (ConnectionDraft.Token(form) is { } token)
                {
                    actions.StoreSecret(SecretKeys.Token(connection.Id), token);
                }

                state = MonitorSession.UpsertConnection(state, connection);
                actions.SaveSettings(state.Settings);
                actions.Refresh(connection.Id);
                return MonitorSession.SetStatus(MonitorSession.OpenBuilds(state), $"Saved {connection.Name}");
            }
            default:
                return state;
        }
    }
}
