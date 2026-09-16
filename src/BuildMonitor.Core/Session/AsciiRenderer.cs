/// <summary>
/// Renders a <see cref="Screen"/> as a fixed width character grid. Pure ASCII so the verified
/// files review as ordinary text diffs, and deterministic on every platform, which is what lets
/// the screen tests run everywhere rather than only where a desktop exists.
/// </summary>
static class AsciiRenderer
{
    const int barWidth = 10;
    const int timingWidth = 10;
    const int providerWidth = 8;
    const int minimumName = 8;
    const int minimumDetail = 12;
    // Past this a long pipeline or branch is cut short rather than pushing every row's chips into
    // the drop down.
    const int maximumDetail = 40;
    // Past this a long name is cut short.
    const int maximumAuthor = 20;
    const string overflow = "[...]";
    // The chips cell is this wide while there is room, so the columns before it do not move as
    // builds gain and lose chips.
    const string widestChips = "PR 9999 [Retry] [Copy log] [Open dir]";
    // Inside the filter box's brackets.
    const int searchWidth = 16;
    // Beside a Directory field's box, where every head draws the button that asks for a folder.
    const string browse = " [ Browse ]";

    public static string Render(Screen screen)
    {
        var columns = Math.Max(40, screen.Columns);
        var inner = columns - 4;
        var builder = new StringBuilder();
        var border = $"+{new string('-', columns - 2)}+";
        builder.Append(border).Append('\n');
        builder.Append(Full(Justify(screen.Title, Subtitle(screen), inner), columns)).Append('\n');
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

    /// <summary>
    /// What the title line says after the title: a form's title, or the builds page's counts then
    /// its filter box, at the right, where the pixel heads put the box in their header.
    /// </summary>
    static string Subtitle(Screen screen) =>
        screen.Builds is { } builds
            ? $"{builds.Header}  Filter: [{Fit(builds.Search, searchWidth)}]"
            : screen.Form?.Title ?? "";

    // Builds page

    static List<string> BuildsLines(BuildsPage page, int inner)
    {
        if (page.Rows.Count == 0)
        {
            // The dots stand in for the spinner the pixel heads turn beside the words.
            return [page.Loading ? $"{page.Empty}..." : page.Empty];
        }

        // "[-] " leads a group's name.
        var longestName = Math.Max(
            page.Names.Select(_ => _.Length).DefaultIfEmpty().Max(),
            page.GroupNames.Select(_ => _.Length + 4).DefaultIfEmpty().Max());
        var longestDetail = page.Details.Select(_ => _.Length).DefaultIfEmpty().Max();
        var author = Math.Min((page.Authors ?? []).Select(_ => _.Length).DefaultIfEmpty().Max(), maximumAuthor);
        var layout = Layout(inner, page.Rows.Any(_ => _.Provider.Length > 0), longestName, longestDetail, author);
        return page.Rows.Select(_ => RowLine(_, layout)).ToList();
    }

    static string DisplayName(BuildRow row) =>
        row.Kind == RowKind.Group
            ? $"{(row.Expanded ? "[-]" : "[+]")} {row.Name}"
            : row.Name;

    /// <summary>
    /// The widths every row shares, so the columns line up. The name column is as wide as the
    /// longest name and the detail column as the longest detail, up to a readable maximum, and the
    /// chips give way first: a row without room for all of them puts the last behind an overflow
    /// chip rather than cutting the names short. Only once no chip but that one fits does the bar
    /// go, and then the names shrink.
    /// </summary>
    static (int Name, int Detail, bool Provider, bool Bar, int Author, int Chips) Layout(int inner, bool provider, int longestName, int longestDetail, int author)
    {
        var name = Math.Max(minimumName, longestName);
        var detail = Math.Min(longestDetail, maximumDetail);
        var bar = true;
        while (true)
        {
            // Marker, glyph and timing, the provider, the bar and the author when shown, and a space
            // before every cell but the first.
            var cells = 2 + timingWidth + (provider ? providerWidth + 1 : 0) + (bar ? barWidth + 1 : 0) + (author > 0 ? author + 1 : 0) + 5;
            // What the name, detail and chips share.
            var available = inner - cells;
            var spare = available - name - detail;
            if (spare >= widestChips.Length)
            {
                return (name, available - name - widestChips.Length, provider, bar, author, widestChips.Length);
            }

            if (spare >= overflow.Length)
            {
                return (name, detail, provider, bar, author, spare);
            }

            if (bar)
            {
                bar = false;
                continue;
            }

            var names = available - overflow.Length;
            var shrunk = Math.Clamp(name, minimumName, Math.Max(minimumName, names - Math.Min(minimumDetail, detail)));
            return (shrunk, Math.Max(1, names - shrunk), provider, false, author, overflow.Length);
        }
    }

    static string RowLine(BuildRow row, (int Name, int Detail, bool Provider, bool Bar, int Author, int Chips) layout)
    {
        var marker = row.Selected ? '>' : ' ';
        var cells = new List<string>
        {
            marker.ToString(),
            Glyph(row.Status).ToString(),
            Fit(DisplayName(row), layout.Name)
        };
        // Beside the pipeline it ran, so a group's members, whose first cell is empty, still say
        // which service each came from.
        if (layout.Provider)
        {
            cells.Add(Fit(row.Provider, providerWidth));
        }

        cells.Add(Fit(row.DetailText, layout.Detail));
        if (layout.Bar)
        {
            cells.Add(Bar(row.Progress));
        }

        cells.Add(Fit(row.Timing, timingWidth));
        if (layout.Author > 0)
        {
            cells.Add(Fit(row.Author, layout.Author));
        }

        cells.Add(Fit(Chips(row.Chips, layout.Chips), layout.Chips));
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

    /// <summary>
    /// The chips that fit, from the left, then an overflow chip in place of the rest. A chip is
    /// drawn only with room left after it for the overflow chip, unless it is the last, so the
    /// overflow chip always fits where the first chip that did not would have gone.
    /// </summary>
    static string Chips(IReadOnlyList<RowChip> chips, int width)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < chips.Count; index++)
        {
            var text = ChipText(chips[index]);
            var start = builder.Length == 0 ? 0 : builder.Length + 1;
            var reserve = index == chips.Count - 1 ? 0 : 1 + overflow.Length;
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (start + text.Length + reserve > width)
            {
                builder.Append(overflow);
                break;
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Links bare and actions bracketed, as the pixel heads colour the two apart.
    /// </summary>
    static string ChipText(RowChip chip) =>
        chip.Kind is ChipKind.Retry or ChipKind.Cancel or ChipKind.CopyLog or ChipKind.OpenDirectory
            ? $"[{chip.Label}]"
            : chip.Label;

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
            case FieldKind.Directory:
            {
                // The button sits beside the box, as every head draws it, so the width it takes is
                // out of the box's rather than off the end of the line.
                var width = Math.Min(40, Math.Max(10, inner - label.Length - 4 - browse.Length));
                var hint = field.Value.Length == 0 && field.Hint is not null ? $" ({field.Hint})" : "";
                return $"{label}[{Fit(field.Value, width)}]{browse}{hint}";
            }
            case FieldKind.Select:
                return $"{label}<{field.Value}>{(field.Enabled ? "" : " (disabled)")}";
            case FieldKind.Button:
                return field.Enabled ? $"[ {field.Label} ]" : $"( {field.Label} )";
            case FieldKind.Link:
                return field.Label == field.Value ? field.Value : $"{field.Label} -> {field.Value}";
            case FieldKind.ListRow:
                return $"- {label}{field.Value} [x]";
            case FieldKind.EditRow:
                return $"- [ {label}{field.Value} ]";
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
    /// over the frame. Anchored one line under its row, inset from the left, or for the drop down
    /// of a row's overflow chip, under that chip.
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
        var left = 4;
        if (menu.Overflow)
        {
            var anchor = lines[top - 1];
            var chip = new string(anchor).LastIndexOf(overflow, StringComparison.Ordinal);
            if (chip >= 0)
            {
                // Kept inside the grid when the chip sits near its right edge.
                left = Math.Max(left, Math.Min(chip, anchor.Length - width - 3));
            }
        }

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
        return string.Join('\n', lines.Select(_ => new string(_)));
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
