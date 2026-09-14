/// <summary>
/// Renders a <see cref="Screen"/> as a fixed width character grid. Pure ASCII so the verified
/// files review as ordinary text diffs, and deterministic on every platform, which is what lets
/// the screen tests run everywhere rather than only where a desktop exists.
/// </summary>
static class AsciiRenderer
{
    const int barWidth = 10;
    const int statusWidth = 9;
    const int timingWidth = 10;
    const int linksWidth = 18;
    const int actionsWidth = 8;
    const int providerWidth = 8;

    public static string Render(Screen screen)
    {
        var columns = Math.Max(40, screen.Columns);
        var inner = columns - 4;
        var builder = new StringBuilder();
        var border = $"+{new string('-', columns - 2)}+";
        builder.Append(border).Append('\n');
        var subtitle = screen.Builds?.Header ?? screen.Form?.Title ?? "";
        builder.Append(Full(Justify(screen.Title, subtitle, inner), columns)).Append('\n');
        builder.Append(border).Append('\n');

        var body = Math.Max(1, screen.Rows - ScreenBuilder.Chrome);
        var lines = screen.Builds is { } builds
            ? BuildsLines(builds, inner)
            : FormLines(screen.Form!, inner);
        for (var index = 0; index < body; index++)
        {
            builder.Append(Full(index < lines.Count ? lines[index] : "", columns)).Append('\n');
        }

        builder.Append(border).Append('\n');
        builder.Append(Full(Justify(Buttons(screen), screen.Status, inner), columns)).Append('\n');
        builder.Append(border);
        var text = Overlay(builder.ToString(), screen);
        var notification = screen.Notification is null
            ? ""
            : $"\nnotify: \"{screen.Notification.Title}\" \"{screen.Notification.Message}\"";
        return $"{text}\n{Tray(screen.Tray)}{notification}";
    }

    // Builds page

    static List<string> BuildsLines(BuildsPage page, int inner)
    {
        if (page is { Loading: true, Rows.Count: 0 })
        {
            return ["Loading builds..."];
        }

        var layout = Layout(inner, page.Rows.Any(_ => _.Provider.Length > 0));
        return page.Rows.Select(_ => RowLine(_, layout)).ToList();
    }

    /// <summary>
    /// The fixed cells around the two name columns, dropped from the right when the window is too
    /// narrow to hold them, so the names always keep a readable width. The first name column takes
    /// the larger share: it holds the repository and branch, the longer of the two.
    /// </summary>
    static (int Name, int Detail, bool Provider, bool Status, bool Bar, bool Links, bool Actions) Layout(int inner, bool provider)
    {
        // marker, glyph, run number: always present.
        const int fixedCells = 1 + 1 + 6;
        var status = true;
        var bar = true;
        var links = true;
        var actions = true;
        while (true)
        {
            var used = fixedCells + timingWidth + (provider ? providerWidth : 0) + (status ? statusWidth : 0) + (bar ? barWidth : 0) + (links ? linksWidth : 0) + (actions ? actionsWidth : 0);
            var cells = 4 + (provider ? 1 : 0) + (status ? 1 : 0) + (bar ? 1 : 0) + (links ? 1 : 0) + (actions ? 1 : 0);
            // One space between each cell, and two name cells.
            var remaining = inner - used - (cells + 1);
            if (remaining >= 28)
            {
                var detail = remaining * 9 / 20;
                return (remaining - detail, detail, provider, status, bar, links, actions);
            }

            if (actions)
            {
                actions = false;
            }
            else if (links)
            {
                links = false;
            }
            else if (bar)
            {
                bar = false;
            }
            else if (status)
            {
                status = false;
            }
            else
            {
                return (Math.Max(8, remaining * 3 / 5), Math.Max(6, remaining - remaining * 3 / 5), provider, false, false, false, false);
            }
        }
    }

    static string RowLine(BuildRow row, (int Name, int Detail, bool Provider, bool Status, bool Bar, bool Links, bool Actions) layout)
    {
        var marker = row.Selected ? '>' : ' ';
        var name = row.Kind == RowKind.Group
            ? $"{(row.Expanded ? "[-]" : "[+]")} {row.Name}"
            : row.Name;
        var cells = new List<string>
        {
            marker.ToString(),
            Glyph(row.Status).ToString(),
            Fit(name, layout.Name)
        };
        // Beside the pipeline it ran, so a group's members, whose first cell is empty, still say
        // which service each came from.
        if (layout.Provider)
        {
            cells.Add(Fit(row.Provider, providerWidth));
        }

        cells.Add(Fit(row.Detail, layout.Detail));
        cells.Add(Fit(row.RunNumber, 6).PadLeft(6));
        if (layout.Status)
        {
            cells.Add(Fit(row.StatusText, statusWidth));
        }

        if (layout.Bar)
        {
            cells.Add(Bar(row.Progress));
        }

        cells.Add(Fit(row.Timing, timingWidth));
        if (layout.Links)
        {
            cells.Add(Fit(Links(row), linksWidth));
        }

        if (layout.Actions)
        {
            cells.Add(Fit(Actions(row), actionsWidth));
        }

        return string.Join(' ', cells);
    }

    static string Bar(double progress)
    {
        if (progress < 0)
        {
            return new(' ', barWidth);
        }

        var filled = (int) Math.Round(progress * (barWidth - 2));
        return $"[{new string('#', filled)}{new string('-', barWidth - 2 - filled)}]";
    }

    static string Links(BuildRow row)
    {
        var parts = new List<string>();
        if (row.Build is not null)
        {
            parts.Add(row.Build.Label);
        }

        if (row.Branch is not null)
        {
            parts.Add(row.Branch.Label);
        }

        if (row.PullRequest is not null)
        {
            parts.Add(row.PullRequest.Label);
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// A build is almost always one or the other: a finished one can be retried, a live one
    /// cancelled. The rare both is abbreviated rather than given a cell wide enough for it.
    /// </summary>
    static string Actions(BuildRow row) =>
        (row.CanRetry, row.CanCancel) switch
        {
            (true, true) => "[R] [C]",
            (true, false) => "[Retry]",
            (false, true) => "[Cancel]",
            _ => ""
        };

    static char Glyph(BuildStatus status) =>
        status switch
        {
            BuildStatus.Queued => '?',
            BuildStatus.Running => '>',
            BuildStatus.Succeeded => '+',
            BuildStatus.Failed => 'x',
            BuildStatus.Cancelled => '-',
            _ => ' '
        };

    // Form pages

    static List<string> FormLines(FormPage form, int inner) =>
        form.Fields.Select(_ => FieldLine(_, inner)).ToList();

    static string FieldLine(Field field, int inner)
    {
        var label = field.Label.Length == 0 ? "" : $"{field.Label}: ";
        switch (field.Kind)
        {
            case FieldKind.Label:
                return $"{label}{field.Value}";
            case FieldKind.Checkbox:
                return $"[{(field.Value == "true" ? 'x' : ' ')}] {field.Label}";
            case FieldKind.Text:
            case FieldKind.Password:
            case FieldKind.Number:
            {
                var width = field.Kind == FieldKind.Number ? 6 : Math.Min(40, Math.Max(10, inner - label.Length - 4));
                var value = field.Kind == FieldKind.Password ? new('*', field.Value.Length) : field.Value;
                var box = $"[{Fit(value, width)}]";
                var hint = field.Value.Length == 0 && field.Hint is not null ? $" ({field.Hint})" : "";
                return $"{label}{box}{hint}{(field.Enabled ? "" : " (disabled)")}";
            }
            case FieldKind.Select:
                return $"{label}<{field.Value}>{(field.Enabled ? "" : " (disabled)")}";
            case FieldKind.Button:
                return field.Enabled ? $"[ {field.Label} ]" : $"( {field.Label} )";
            case FieldKind.Link:
                return field.Label == field.Value ? field.Value : $"{field.Label} -> {field.Value}";
            case FieldKind.ListRow:
                return $"- {label}{field.Value} [x]";
            default:
                return $"{label}{field.Value}";
        }
    }

    // Chrome

    static string Buttons(Screen screen)
    {
        var builder = new StringBuilder();
        foreach (var button in screen.Buttons)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // Disabled buttons keep their slot so the footer does not reflow.
            builder.Append(button.Enabled ? '[' : '(');
            builder.Append(button.Label);
            builder.Append(button.Enabled ? ']' : ')');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The open context menu, drawn over the finished grid the way the pixel heads float theirs
    /// over the frame. Anchored one line under its row, inset from the left.
    /// </summary>
    static string Overlay(string text, Screen screen)
    {
        if (screen.Menu is not { } menu ||
            menu.Labels.Count == 0)
        {
            return text;
        }

        var lines = text.Split('\n').Select(_ => _.ToCharArray()).ToList();
        var width = menu.Labels.Max(_ => _.Length) + 2;
        // Border, title, separator: three lines sit above the first body row.
        var top = 3 + menu.Row + 1;
        const int left = 4;

        void Write(int line, string content)
        {
            if (line < 0 ||
                line >= lines.Count)
            {
                return;
            }

            var row = lines[line];
            for (var index = 0; index < content.Length && left + index < row.Length - 1; index++)
            {
                row[left + index] = content[index];
            }
        }

        var border = $"+{new string('-', width)}+";
        Write(top, border);
        for (var index = 0; index < menu.Labels.Count; index++)
        {
            Write(top + 1 + index, $"| {menu.Labels[index].PadRight(width - 2)} |");
        }

        Write(top + 1 + menu.Labels.Count, border);
        return string.Join("\n", lines.Select(_ => new string(_)));
    }

    /// <summary>
    /// The tray state, under the window, so a snapshot describes the whole app.
    /// </summary>
    static string Tray(TrayModel tray)
    {
        var builder = new StringBuilder();
        builder.Append($"tray: {tray.Icon} \"{tray.Tooltip}\"");
        foreach (var item in tray.Items)
        {
            builder.Append('\n');
            if (item.Separator)
            {
                builder.Append("  ---");
                continue;
            }

            builder.Append("  ");
            builder.Append(item.Enabled ? item.Label : $"({item.Label})");
            if (item.Children is { Count: > 0 })
            {
                builder.Append(" > ");
                builder.Append(string.Join(" | ", item.Children.Select(_ => _.Label)));
            }
        }

        return builder.ToString();
    }

    static string Full(string content, int columns) =>
        $"| {Fit(content, columns - 4)} |";

    static string Justify(string left, string right, int width)
    {
        if (right.Length == 0)
        {
            return Fit(left, width);
        }

        var gap = width - right.Length - left.Length;
        if (gap < 1)
        {
            return Fit($"{left} {right}", width);
        }

        return $"{left}{new string(' ', gap)}{right}";
    }

    static string Fit(string text, int width)
    {
        var flat = text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        if (flat.Length == width)
        {
            return flat;
        }

        if (flat.Length < width)
        {
            return flat.PadRight(width);
        }

        if (width <= 1)
        {
            return ">";
        }

        return $"{flat.AsSpan(0, width - 1)}>";
    }
}
