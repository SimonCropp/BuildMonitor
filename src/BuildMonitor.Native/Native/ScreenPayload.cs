/// <summary>
/// Flattens a <see cref="Screen"/> into the blittable frame bm.h describes. The buffers are
/// reused across frames and pinned only for the duration of the call.
/// </summary>
sealed unsafe class ScreenPayload
{
    static byte[] emptyStrings = [0];
    List<byte> strings = [];
    List<BmRow> rows = [];
    List<BmString> names = [];
    List<BmString> details = [];
    List<BmString> authors = [];
    List<BmChip> chips = [];
    List<BmSpan> spans = [];
    List<BmTooltip> tooltips = [];
    List<BmField> fields = [];
    List<BmString> options = [];
    List<BmButton> buttons = [];
    List<BmMenuItem> menu = [];
    List<BmTrayItem> trayItems = [];
    BmScreen screen;
    // Counts the builds, so the native side can tell a new screen from the one it drew last and
    // draws nothing while it is handed the same one.
    int generation;

    /// <summary>
    /// The ids behind the indexes a head reports: fields and tray items of the last frame.
    /// </summary>
    public List<string> FieldIds { get; } = [];

    public List<string> TrayItemIds { get; } = [];

    public Page LastPage { get; private set; }

    /// <summary>
    /// For the tests: the generation the last build handed over.
    /// </summary>
    public int Generation => screen.Generation;

    public void Build(Screen source)
    {
        strings.Clear();
        rows.Clear();
        names.Clear();
        details.Clear();
        authors.Clear();
        chips.Clear();
        spans.Clear();
        tooltips.Clear();
        fields.Clear();
        options.Clear();
        buttons.Clear();
        menu.Clear();
        trayItems.Clear();
        FieldIds.Clear();
        TrayItemIds.Clear();
        LastPage = source.Page;

        screen = new()
        {
            Page = source.Builds is null ? 1 : 0,
            Title = Add(source.Title),
            Status = Add(source.Status),
            StatusTooltip = Add(source.StatusTooltip),
            ScrollTop = 0,
            TotalRows = 0,
            SelectedRow = -1,
            MenuRow = -1,
            TrayIcon = (int) source.Tray.Icon,
            TrayTooltip = Add(source.Tray.Tooltip),
            Theme = (int) source.Theme,
            Generation = ++generation
        };

        if (source.Builds is { } builds)
        {
            screen.Header = Add(builds.Header);
            screen.ScrollTop = builds.ScrollTop;
            screen.TotalRows = builds.TotalRows;
            screen.SelectedRow = builds.SelectedRow;
            screen.Loading = builds.Loading ? 1 : 0;
            screen.Search = Add(builds.Search);
            screen.SearchTooltip = Add(builds.SearchTooltip);
            screen.Empty = Add(builds.Empty);
            screen.NameCount = builds.Names.Count;
            screen.GroupNameCount = builds.GroupNames.Count;
            foreach (var name in builds.Names.Concat(builds.GroupNames))
            {
                names.Add(Add(name));
            }

            foreach (var detail in builds.Details)
            {
                details.Add(Add(detail));
            }

            foreach (var author in builds.Authors ?? [])
            {
                authors.Add(Add(author));
            }

            foreach (var row in builds.Rows)
            {
                var chipOffset = chips.Count;
                foreach (var chip in row.Chips)
                {
                    chips.Add(
                        new()
                        {
                            Label = Add(chip.Label),
                            Tooltip = Add(chip.Tooltip),
                            Icon = Add(chip.Icon),
                            Text = Add(chip.Text),
                            Kind = (int) chip.Kind
                        });
                }

                var spanOffset = spans.Count;
                foreach (var span in row.Detail)
                {
                    spans.Add(
                        new()
                        {
                            Text = Add(span.Text),
                            Link = (int) span.Link
                        });
                }

                var tooltipOffset = tooltips.Count;
                foreach (var tooltip in row.Tooltips)
                {
                    tooltips.Add(
                        new()
                        {
                            Text = Add(tooltip.Text),
                            Part = (int) tooltip.Part
                        });
                }

                rows.Add(
                    new()
                    {
                        Status = (int) row.Status,
                        Flags = (row.Selected ? BmFlags.RowSelected : 0) |
                                (row.Kind == RowKind.Group ? BmFlags.RowGroup : 0) |
                                (row.Expanded ? BmFlags.RowExpanded : 0) |
                                (row.Kind == RowKind.Member ? BmFlags.RowMember : 0),
                        Name = Add(row.Name),
                        Detail = Add(row.DetailText),
                        NameIcon = Add(row.NameIcon),
                        DetailIcon = Add(row.DetailIcon),
                        Timing = Add(row.Timing),
                        ChipOffset = chipOffset,
                        ChipCount = row.Chips.Count,
                        Progress = (float) row.Progress,
                        NameLink = (int) row.NameLink,
                        DetailIconLink = (int) row.DetailIconLink,
                        StatusLink = (int) row.StatusLink,
                        SpanOffset = spanOffset,
                        SpanCount = row.Detail.Count,
                        Author = Add(row.Author),
                        TooltipOffset = tooltipOffset,
                        TooltipCount = row.Tooltips.Count
                    });
            }
        }

        if (source.Form is { } form)
        {
            screen.FormTitle = Add(form.Title);
            foreach (var field in form.Fields)
            {
                var optionOffset = options.Count;
                if (field.Options is not null)
                {
                    foreach (var option in field.Options)
                    {
                        options.Add(Add(option));
                    }
                }

                FieldIds.Add(field.Id);
                fields.Add(
                    new()
                    {
                        Kind = (int) field.Kind,
                        Flags = field.Enabled ? BmFlags.FieldEnabled : 0,
                        Id = Add(field.Id),
                        Label = Add(field.Label),
                        Value = Add(field.Value),
                        Hint = Add(field.Hint ?? ""),
                        Note = Add(field.Note ?? ""),
                        OptionOffset = optionOffset,
                        OptionCount = field.Options?.Count ?? 0
                    });
            }
        }

        foreach (var button in source.Buttons)
        {
            buttons.Add(
                new()
                {
                    Label = Add(button.Label),
                    Tooltip = Add(button.Tooltip),
                    Flags = button.Enabled ? BmFlags.ButtonEnabled : 0
                });
        }

        if (source.Menu is { } overlay)
        {
            screen.MenuRow = overlay.Row;
            screen.MenuOverflow = overlay.Overflow ? 1 : 0;
            foreach (var item in overlay.Items)
            {
                menu.Add(
                    new()
                    {
                        Label = Add(item.Label),
                        Flags = item.SeparatorAbove ? BmFlags.MenuSeparatorAbove : 0
                    });
            }
        }

        foreach (var item in source.Tray.Items)
        {
            TrayItemIds.Add(item.Id);
            trayItems.Add(
                new()
                {
                    Id = Add(item.Id),
                    Label = Add(item.Label),
                    Icon = Add(item.IconName ?? ""),
                    Flags = (item.Enabled ? BmFlags.TrayEnabled : 0) | (item.Separator ? BmFlags.TraySeparator : 0)
                });
        }
    }

    BmString Add(string text)
    {
        var offset = strings.Count;
        var bytes = Encoding.UTF8.GetBytes(text);
        strings.AddRange(bytes);
        return new()
        {
            Offset = offset,
            Length = bytes.Length
        };
    }

    public int Present() =>
        WithPinned(Bm.Present);

    public int Capture(int width, int height, string pngPath) =>
        WithPinned(_ => Bm.Capture(_, width, height, pngPath));

    delegate int Call(BmScreen* screen);

    int WithPinned(Call call)
    {
        var stringBytes = strings.Count == 0 ? emptyStrings.AsSpan() : CollectionsMarshal.AsSpan(strings);
        fixed (byte* stringPointer = stringBytes)
        fixed (BmRow* rowPointer = CollectionsMarshal.AsSpan(rows))
        fixed (BmString* namePointer = CollectionsMarshal.AsSpan(names))
        fixed (BmString* detailPointer = CollectionsMarshal.AsSpan(details))
        fixed (BmString* authorPointer = CollectionsMarshal.AsSpan(authors))
        fixed (BmChip* chipPointer = CollectionsMarshal.AsSpan(chips))
        fixed (BmSpan* spanPointer = CollectionsMarshal.AsSpan(spans))
        fixed (BmTooltip* tooltipPointer = CollectionsMarshal.AsSpan(tooltips))
        fixed (BmField* fieldPointer = CollectionsMarshal.AsSpan(fields))
        fixed (BmString* optionPointer = CollectionsMarshal.AsSpan(options))
        fixed (BmButton* buttonPointer = CollectionsMarshal.AsSpan(buttons))
        fixed (BmMenuItem* menuPointer = CollectionsMarshal.AsSpan(menu))
        fixed (BmTrayItem* trayPointer = CollectionsMarshal.AsSpan(trayItems))
        {
            var frame = screen;
            frame.Strings = stringPointer;
            frame.StringsLength = strings.Count;
            frame.Rows = rowPointer;
            frame.RowCount = rows.Count;
            frame.Names = namePointer;
            frame.Details = detailPointer;
            frame.DetailCount = details.Count;
            frame.Authors = authorPointer;
            frame.AuthorCount = authors.Count;
            frame.Chips = chipPointer;
            frame.ChipCount = chips.Count;
            frame.Spans = spanPointer;
            frame.SpanCount = spans.Count;
            frame.Tooltips = tooltipPointer;
            frame.TooltipCount = tooltips.Count;
            frame.Fields = fieldPointer;
            frame.FieldCount = fields.Count;
            frame.Options = optionPointer;
            frame.OptionCount = options.Count;
            frame.Buttons = buttonPointer;
            frame.ButtonCount = buttons.Count;
            frame.Menu = menuPointer;
            frame.MenuCount = menu.Count;
            frame.TrayItems = trayPointer;
            frame.TrayItemCount = trayItems.Count;
            return call(&frame);
        }
    }

    /// <summary>
    /// For the tests: the strings blob and the flattened tray, as text.
    /// </summary>
    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"page: {screen.Page} rows: {rows.Count} details: {details.Count} chips: {chips.Count} spans: {spans.Count} fields: {fields.Count} options: {options.Count} buttons: {buttons.Count} menu: {menu.Count} tray: {trayItems.Count} strings: {strings.Count} bytes");
        var blob = strings.ToArray();
        string Text(BmString value) => Encoding.UTF8.GetString(blob, value.Offset, value.Length);
        builder.AppendLine($"search='{Text(screen.Search)}' tip='{Text(screen.SearchTooltip)}' empty='{Text(screen.Empty)}'");
        builder.AppendLine($"status='{Text(screen.Status)}' tip='{Text(screen.StatusTooltip).Replace('\n', '/')}'");
        foreach (var row in rows)
        {
            var rowChips = chips.Skip(row.ChipOffset).Take(row.ChipCount).Select(_ => $"{(ChipKind) _.Kind}:{Text(_.Label)}[{Text(_.Icon)}|{Text(_.Text)}]");
            var rowSpans = spans.Skip(row.SpanOffset).Take(row.SpanCount).Select(_ => $"{(ChipKind) _.Link}:'{Text(_.Text)}'");
            builder.AppendLine($"row status={row.Status} flags={row.Flags} progress={row.Progress:0.00} '{Text(row.Name)}' link={(ChipKind) row.NameLink} status={(ChipKind) row.StatusLink} '{Text(row.Detail)}' spans={string.Join(',', rowSpans)} icon='{Text(row.NameIcon)}' detail={(ChipKind) row.DetailIconLink}:'{Text(row.DetailIcon)}' '{Text(row.Timing)}' author='{Text(row.Author)}' chips={string.Join(',', rowChips)}");
            // A line each, and on their own lines: a tooltip runs to a sentence, and several of
            // them joined onto the row would put what a link opens past the width of the snapshot.
            foreach (var tooltip in tooltips.Skip(row.TooltipOffset).Take(row.TooltipCount))
            {
                builder.AppendLine($"  tip {(RowPart) tooltip.Part}: {Text(tooltip.Text).Replace('\n', '/')}");
            }

            foreach (var chip in chips.Skip(row.ChipOffset).Take(row.ChipCount))
            {
                builder.AppendLine($"  tip {(ChipKind) chip.Kind} chip: {Text(chip.Tooltip)}");
            }
        }

        foreach (var field in fields)
        {
            builder.AppendLine($"field kind={field.Kind} flags={field.Flags} '{Text(field.Id)}' '{Text(field.Label)}' '{Text(field.Value)}' options={field.OptionOffset}+{field.OptionCount}");
        }

        foreach (var button in buttons)
        {
            builder.AppendLine($"button flags={button.Flags} '{Text(button.Label)}' tip='{Text(button.Tooltip)}'");
        }

        foreach (var item in menu)
        {
            builder.AppendLine($"menu flags={item.Flags} '{Text(item.Label)}'");
        }

        foreach (var item in trayItems)
        {
            builder.AppendLine($"tray flags={item.Flags} '{Text(item.Id)}' '{Text(item.Label)}' icon='{Text(item.Icon)}'");
        }

        return builder.ToString();
    }
}
