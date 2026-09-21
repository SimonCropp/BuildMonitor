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

        // The last message has been seen, unless the input is the click that copies it: cleared
        // first, the copy would take whatever the footer falls back to, "Polled 3s ago".
        if (input.Any &&
            input.Key != CommandKind.CopyStatus)
        {
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
            var (chosen, command, target) = MonitorSession.ChooseMenuItem(state, input.ClickedMenuItem);
            state = Execute(chosen, command, target, actions, window);
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
            state = ClickField(state, field, actions, window);
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
        // The folder and the repository are what a group's row carries, since both resolve its
        // members rather than one build. Every other command reads the row's build, so a group
        // would do nothing anyway, and letting one through would have a stale frame's click toggle
        // the group.
        if (row < 0 ||
            row >= rows.Length ||
            (rows[row].Build is null && chip is not (ChipKind.OpenDirectory or ChipKind.Repo)))
        {
            return state;
        }

        return Execute(MonitorSession.SelectRow(state, row), RowChips.Command(chip), null, actions, window);
    }

    /// <summary>
    /// The window is passed on only where a field needs it. Browse is the one that does: a folder
    /// chooser belongs to the toolkit, the way the clipboard does.
    /// </summary>
    /// <summary>
    /// What a click on a form's field does, by the field's id. The one rule: a field used to carry
    /// the command as well, and nothing read it, so the two could say different things and only
    /// this one was ever obeyed.
    /// </summary>
    static SessionState ClickField(SessionState state, string id, MonitorActions actions, IMonitorWindow? window)
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
            // A click on a Directory field is its Browse button: the box itself reports edits, not
            // clicks.
            case FormFields.CodeDirectory:
                return Execute(state, CommandKind.BrowseCodeDirectory, null, actions, window);
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
            case FormFields.TokenHelp when state.Form is ConnectionFormState form:
                actions.OpenUrl(form.Descriptor.TokenHelpUrl);
                return state;
            case FormFields.ProviderDocs when state.Form is ConnectionFormState form:
                actions.OpenUrl(form.Descriptor.DocsUrl);
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
            TrayMenu.CodeDirectory => Execute(state, CommandKind.OpenCodeDirectory, null, actions, window),
            TrayMenu.Logs => Execute(state, CommandKind.OpenLogs, null, actions, window),
            TrayMenu.Issue => Execute(state, CommandKind.RaiseIssue, null, actions, window),
            // With the window, because this one now has something to show rather than something
            // to do: a menu item that opened nothing would look like it had done nothing.
            TrayMenu.Update => Show(Execute(state, CommandKind.Update, null, actions, window), window),
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
            case CommandKind.OpenPipeline:
            case CommandKind.OpenRepo:
            {
                // A group has no one build to open, so Enter or a double click opens or closes it.
                if (command == CommandKind.OpenBuild &&
                    MonitorSession.SelectedRow(state) is { Kind: RowKind.Group, Group: { } group })
                {
                    return MonitorSession.ToggleGroup(state, group);
                }

                // A group's name is its members' repository, which every member of a group agrees
                // on, so the first of them answers for the row that has no build of its own.
                var selected = MonitorSession.SelectedBuild(state);
                if (selected is null &&
                    command == CommandKind.OpenRepo &&
                    MonitorSession.SelectedRow(state) is { Kind: RowKind.Group, Members: [var first, ..] })
                {
                    selected = first;
                }

                if (selected is not { } build)
                {
                    return state;
                }

                var url = command switch
                {
                    CommandKind.OpenBranch => build.BranchUrl,
                    CommandKind.OpenPullRequest => build.PullRequestUrl,
                    CommandKind.OpenPipeline => build.PipelineUrl,
                    CommandKind.OpenRepo => build.RepoUrl,
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
                    // Straight at the window rather than through the state, because a URL is
                    // already in hand and has nothing to fetch. That skips ClipboardPump's retry,
                    // so the one thing it has to keep is the honest status.
                    if (window is not null &&
                        !window.SetClipboard(copied.BuildUrl))
                    {
                        return MonitorSession.SetStatus(state, MonitorSession.ClipboardBusy);
                    }

                    return MonitorSession.SetStatus(state, "Copied build URL");
                }

                return state;
            case CommandKind.CopyUserCode:
                return MonitorSession.CopyUserCode(state);
            case CommandKind.CopyStatus:
                // The footer as shown, and left as it is: a status saying it was copied would replace
                // the error being copied, often before it was read in full.
                if (ScreenBuilder.Status(state, DateTimeOffset.UtcNow) is { Length: > 0 } status)
                {
                    return state with { Clipboard = status };
                }

                return state;
            case CommandKind.CopyLog:
                if (MonitorSession.SelectedBuild(state) is { } logged)
                {
                    return CopyLog(state, logged, actions);
                }

                return state;
            case CommandKind.Triage:
                if (MonitorSession.SelectedBuild(state) is { } triaged)
                {
                    return Triage(state, triaged, actions);
                }

                return state;
            case CommandKind.Retry:
                if (MonitorSession.SelectedBuild(state) is { } retry)
                {
                    return Retry(state, retry, actions);
                }

                return state;
            case CommandKind.Cancel:
                if (MonitorSession.SelectedBuild(state) is { } cancel)
                {
                    return Cancel(state, cancel, actions);
                }

                return state;
            case CommandKind.RunNext:
                if (MonitorSession.SelectedBuild(state) is { } next)
                {
                    return RunNext(state, next, actions);
                }

                return state;
            case CommandKind.OpenRepoDirectory:
            {
                // Resolved through the same lookup that decided to offer the chip, so the two
                // cannot disagree about which checkout the row belongs to. A group's row resolves
                // its members, which offer it only while they are all the one checkout.
                if (MonitorSession.SelectedRow(state) is not { } opening ||
                    LocalRepos.Shared(state.LocalRepos, opening.Builds) is not { } directory)
                {
                    return state;
                }

                actions.OpenDirectory(directory);
                return MonitorSession.SetStatus(state, $"Opened {directory}");
            }
            case CommandKind.BrowseCodeDirectory:
            {
                if (state.Form is not OptionsFormState browsing ||
                    window?.PickDirectory(Started(browsing)) is not { } picked)
                {
                    return state;
                }

                return MonitorSession.FieldChanged(state, FormFields.CodeDirectory, picked);
            }
            case CommandKind.Refresh:
                actions.Refresh(null);
                return MonitorSession.SetStatus(state, "Refreshing");
            case CommandKind.ToggleGroup:
                if (MonitorSession.SelectedRow(state)?.Group is { } toggled)
                {
                    return MonitorSession.ToggleGroup(state, toggled);
                }

                return state;
            case CommandKind.GroupByPrefix:
            {
                if (target is not { Length: > 0 } prefix)
                {
                    return state;
                }

                var grouped = MonitorSession.GroupByPrefix(state, prefix);
                actions.SaveSettings(grouped.Settings);
                return MonitorSession.SetStatus(grouped, $"Grouping by {prefix}");
            }
            case CommandKind.RemoveGroupPrefix:
            {
                if (target is not { Length: > 0 } removed)
                {
                    return state;
                }

                var ungrouped = MonitorSession.RemoveGroupPrefix(state, removed);
                actions.SaveSettings(ungrouped.Settings);
                return MonitorSession.SetStatus(ungrouped, $"Stopped grouping by {removed}");
            }
            case CommandKind.ExcludePipeline:
            {
                if (MonitorSession.SelectedBuild(state) is not { } build)
                {
                    return state;
                }

                var noun = MonitorSession.PipelineNoun(state, build);
                return Excluded(MonitorSession.ExcludePipeline(state, build), $"{build.PipelineName} {noun}", actions);
            }
            case CommandKind.ExcludeBranch:
            {
                if (MonitorSession.SelectedBuild(state) is not { Branch: { } branch } build)
                {
                    return state;
                }

                return Excluded(MonitorSession.ExcludeBranch(state, build), $"{branch} branch", actions);
            }
            case CommandKind.ExcludeRepo:
            {
                if (MonitorSession.SelectedBuild(state) is not { } build)
                {
                    return state;
                }

                return Excluded(MonitorSession.ExcludeRepo(state, build), $"{build.RepoName} repo", actions);
            }
            case CommandKind.ExcludeOrg:
            {
                if (MonitorSession.SelectedBuild(state) is not { } build ||
                    MonitorSession.Org(state, build) is not { } org)
                {
                    return state;
                }

                return Excluded(MonitorSession.ExcludeOrg(state, build), $"{org.Name} {org.Noun}", actions);
            }
            case CommandKind.OpenBuilds:
                return MonitorSession.OpenBuilds(state);
            case CommandKind.OpenOptions:
                return MonitorSession.OpenOptions(state);
            case CommandKind.OpenFilters:
                return MonitorSession.OpenFilters(state);
            case CommandKind.AddConnection:
                return MonitorSession.OpenAddConnection(state, Guid.NewGuid().ToString("N"));
            case CommandKind.EditConnection:
            {
                var id = target ?? MonitorSession.SelectedRow(state)?.Connection?.Connection.Id;
                if (id is null)
                {
                    return state;
                }

                return MonitorSession.OpenEditConnection(state, id);
            }
            case CommandKind.RemoveConnection:
            {
                if (state.Form is not EditConnectionFormState removing)
                {
                    return state;
                }

                var id = removing.ConnectionId;
                state = MonitorSession.OpenBuilds(MonitorSession.RemoveConnection(state, id));
                actions.DeleteSecret(SecretKeys.Token(id));
                actions.DeleteSecret(SecretKeys.Refresh(id));
                actions.SaveSettings(state.Settings);
                return MonitorSession.SetStatus(state, "Connection removed");
            }
            case CommandKind.AddFilter:
                return MonitorSession.AddFilter(state);
            case CommandKind.RemoveFilter:
                if (int.TryParse(target, out var index))
                {
                    return MonitorSession.RemoveFilter(state, index);
                }

                return state;
            case CommandKind.SignIn:
            {
                if (state.Form is not ConnectionFormState form)
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
                if (state.Form is not ConnectionFormState form)
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
                // A new connection that signed in before being abandoned leaves a token behind. An
                // edited one's token is the connection's own, and stays.
                if (state.Form is AddConnectionFormState { SignedIn: true } abandoned)
                {
                    actions.DeleteSecret(SecretKeys.Token(abandoned.ConnectionId));
                    actions.DeleteSecret(SecretKeys.Refresh(abandoned.ConnectionId));
                }

                return MonitorSession.OpenBuilds(state);
            }
            case CommandKind.OpenLogs:
                actions.OpenLogs();
                return state;
            case CommandKind.OpenCodeDirectory:
                // Only offered while the option holds something, so an empty one here is a menu
                // built before a save that cleared it.
                if (state.Settings.CodeDirectory is { Length: > 0 } code)
                {
                    actions.OpenDirectory(code);
                }

                return state;
            case CommandKind.RaiseIssue:
                actions.RaiseIssue();
                return state;
            case CommandKind.Update:
                // Listed here rather than by the page, which is a pure projection of the state and
                // is built every frame: processes are not something to enumerate sixty times a
                // second, and the set warned about should be the set the user agreed to.
                return MonitorSession.OpenUpdate(state, actions.RunningServers());
            case CommandKind.ConfirmUpdate:
                actions.Update();
                // Returned rather than left to the action, which is what makes it happen: a quit
                // swapped into the host from inside an action is undone the moment this returns.
                return MonitorSession.Quit(state);
            case CommandKind.Hide:
                window?.SetHidden(true);
                return MonitorSession.Hide(state);
            case CommandKind.Quit:
                return MonitorSession.Quit(state);
            default:
                return state;
        }
    }

    /// <summary>
    /// Where the chooser opens: what is typed in the field, so a second Browse starts where the
    /// first one left off rather than at the desktop's idea of home.
    /// </summary>
    static string? Started(OptionsFormState form)
    {
        if (form.Value(FormFields.CodeDirectory) is { Length: > 0 } typed)
        {
            return typed;
        }

        return null;
    }

    /// <summary>
    /// Saves the filters an exclusion added and names what it dropped: the rows it was asked on are
    /// gone by the time the status is read, so nothing else on the screen says.
    /// </summary>
    static SessionState Excluded(SessionState state, string what, MonitorActions actions)
    {
        actions.SaveSettings(state.Settings);
        return MonitorSession.SetStatus(state, $"Excluded {what}");
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

    /// <summary>
    /// Gated again here rather than trusted from the chip that was drawn, as <see cref="Triage"/>
    /// is: a poll between the frame and the click can have started the build, and a build already
    /// running has no place in a queue to be moved to.
    /// </summary>
    static SessionState RunNext(SessionState state, Build build, MonitorActions actions)
    {
        if (MonitorSession.Descriptor(state, build) is not { } descriptor ||
            !build.CanRunNext(descriptor))
        {
            return state;
        }

        actions.RunNext(build);
        // The name is composed first rather than interpolated in place: a queued build often has no
        // number yet, and trimming the end cannot reach a gap with the rest of the sentence after it.
        var name = $"{build.PipelineName} {build.RunNumberLabel()}".TrimEnd();
        return MonitorSession.SetStatus(state, $"Moving {name} to the front of the queue");
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

    /// <summary>
    /// Gated again here rather than trusted from the chip that was drawn: a poll between the frame
    /// and the click can have finished the build green, or the watcher can have lost the checkout,
    /// and either leaves a download with nothing to say.
    /// </summary>
    static SessionState Triage(SessionState state, Build build, MonitorActions actions)
    {
        if (!build.LogCopyable() ||
            LocalRepos.Find(state.LocalRepos, build) is null)
        {
            return state;
        }

        actions.Triage(build);
        return MonitorSession.SetStatus(state, $"Collecting {build.PipelineName} {build.RunNumberLabel()} for triage".TrimEnd());
    }

    static SessionState Save(SessionState state, MonitorActions actions) =>
        state.Form switch
        {
            OptionsFormState form => SaveOptions(state, form, actions),
            FiltersFormState form => SaveFilters(state, form, actions),
            AddConnectionFormState form => SaveConnection(state, form, actions, MonitorSession.AddConnection),
            EditConnectionFormState form => SaveConnection(state, form, actions, MonitorSession.ReplaceConnection),
            _ => state
        };

    static SessionState SaveOptions(SessionState state, OptionsFormState form, MonitorActions actions)
    {
        if (!OptionsDraft.TryBuild(form, state.Settings, out var settings, out var error))
        {
            return MonitorSession.SetFormError(state, error);
        }

        string? runAtLoginError = null;
        if (settings.RunAtStartup != state.Settings.RunAtStartup)
        {
            runAtLoginError = actions.SetRunAtLogin(settings.RunAtStartup);
        }

        state = MonitorSession.ApplySettings(state, settings);
        actions.SaveSettings(settings);
        // The rest of the options are saved either way: one of them not taking is worth
        // saying, and is not worth throwing the others away over.
        return MonitorSession.SetStatus(MonitorSession.OpenBuilds(state), runAtLoginError ?? "Options saved");
    }

    static SessionState SaveFilters(SessionState state, FiltersFormState form, MonitorActions actions)
    {
        state = MonitorSession.ApplySettings(state, state.Settings with { Filters = form.Filters });
        actions.SaveSettings(state.Settings);
        return MonitorSession.SetStatus(MonitorSession.OpenBuilds(state), "Filters saved");
    }

    /// <summary>
    /// Each editor passes how its connection goes into the settings, rather than this looking up
    /// whether one with the id exists: a lookup is a guess at which page the save came from.
    /// </summary>
    static SessionState SaveConnection(SessionState state, ConnectionFormState form, MonitorActions actions, Func<SessionState, Connection, SessionState> apply)
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

        state = apply(state, connection);
        actions.SaveSettings(state.Settings);
        actions.Refresh(connection.Id);
        return MonitorSession.SetStatus(MonitorSession.OpenBuilds(state), $"Saved {connection.Name}");
    }
}
