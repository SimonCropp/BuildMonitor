/// <summary>
/// Flattens a <see cref="Screen"/> into the blittable frame bm.h describes. The buffers are
/// reused across frames and pinned only for the duration of the call.
/// </summary>
sealed unsafe class ScreenPayload
{
    readonly List<byte> strings = [];
    readonly List<BmRow> rows = [];
    readonly List<BmString> names = [];
    readonly List<BmField> fields = [];
    readonly List<BmString> options = [];
    readonly List<BmButton> buttons = [];
    readonly List<BmMenuItem> menu = [];
    readonly List<BmTrayItem> trayItems = [];
    BmScreen screen;

    /// <summary>
    /// The ids behind the indexes a head reports: fields and tray items of the last frame.
    /// </summary>
    public List<string> FieldIds { get; } = [];

    public List<string> TrayItemIds { get; } = [];

    public Page LastPage { get; private set; }

    public void Build(Screen source)
    {
        strings.Clear();
        rows.Clear();
        names.Clear();
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
            ScrollTop = 0,
            TotalRows = 0,
            SelectedRow = -1,
            MenuRow = -1,
            TrayIcon = (int) source.Tray.Icon,
            TrayTooltip = Add(source.Tray.Tooltip),
            Theme = (int) source.Theme
        };

        if (source.Builds is { } builds)
        {
            screen.Header = Add(builds.Header);
            screen.ScrollTop = builds.ScrollTop;
            screen.TotalRows = builds.TotalRows;
            screen.SelectedRow = builds.SelectedRow;
            screen.Loading = builds.Loading ? 1 : 0;
            screen.NameCount = builds.Names.Count;
            screen.GroupNameCount = builds.GroupNames.Count;
            foreach (var name in builds.Names.Concat(builds.GroupNames))
            {
                names.Add(Add(name));
            }
            foreach (var row in builds.Rows)
            {
                rows.Add(new()
                {
                    Status = (int) row.Status,
                    Flags = (row.Selected ? BmFlags.RowSelected : 0) |
                            (row.Kind == RowKind.Group ? BmFlags.RowGroup : 0) |
                            (row.Expanded ? BmFlags.RowExpanded : 0) |
                            (row.Kind == RowKind.Member ? BmFlags.RowMember : 0) |
                            (row.CanRetry ? BmFlags.RowCanRetry : 0) |
                            (row.CanCancel ? BmFlags.RowCanCancel : 0),
                    Name = Add(row.Name),
                    Detail = Add(row.Detail),
                    Provider = Add(row.Provider),
                    RunNumber = Add(row.RunNumber),
                    Timing = Add(row.Timing),
                    BuildLabel = Add(row.Build?.Label ?? ""),
                    BranchLabel = Add(row.Branch?.Label ?? ""),
                    PullRequestLabel = Add(row.PullRequest?.Label ?? ""),
                    Progress = (float) row.Progress
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
                fields.Add(new()
                {
                    Kind = (int) field.Kind,
                    Flags = field.Enabled ? BmFlags.FieldEnabled : 0,
                    Id = Add(field.Id),
                    Label = Add(field.Label),
                    Value = Add(field.Value),
                    Hint = Add(field.Hint ?? ""),
                    OptionOffset = optionOffset,
                    OptionCount = field.Options?.Count ?? 0
                });
            }
        }

        foreach (var button in source.Buttons)
        {
            buttons.Add(new()
            {
                Label = Add(button.Label),
                Flags = button.Enabled ? BmFlags.ButtonEnabled : 0
            });
        }

        if (source.Menu is { } overlay)
        {
            screen.MenuRow = overlay.Row;
            foreach (var label in overlay.Labels)
            {
                menu.Add(new() { Label = Add(label) });
            }
        }

        foreach (var item in source.Tray.Items)
        {
            AddTrayItem(item, 0);
        }
    }

    void AddTrayItem(TrayMenuItem item, int depth)
    {
        TrayItemIds.Add(item.Id);
        trayItems.Add(new()
        {
            Id = Add(item.Id),
            Label = Add(item.Label),
            Icon = Add(item.IconName ?? ""),
            Flags = (item.Enabled ? BmFlags.TrayEnabled : 0) | (item.Separator ? BmFlags.TraySeparator : 0),
            Depth = depth
        });
        if (item.Children is null)
        {
            return;
        }

        foreach (var child in item.Children)
        {
            AddTrayItem(child, depth + 1);
        }
    }

    BmString Add(string text)
    {
        var offset = strings.Count;
        var bytes = Encoding.UTF8.GetBytes(text);
        strings.AddRange(bytes);
        return new() { Offset = offset, Length = bytes.Length };
    }

    public int Present() =>
        WithPinned(Bm.Present);

    public int Capture(int width, int height, string pngPath) =>
        WithPinned(_ => Bm.Capture(_, width, height, pngPath));

    delegate int Call(BmScreen* screen);

    int WithPinned(Call call)
    {
        var stringBytes = strings.Count == 0 ? [0] : CollectionsMarshal.AsSpan(strings).ToArray();
        var rowArray = rows.ToArray();
        var nameArray = names.ToArray();
        var fieldArray = fields.ToArray();
        var optionArray = options.ToArray();
        var buttonArray = buttons.ToArray();
        var menuArray = menu.ToArray();
        var trayArray = trayItems.ToArray();
        fixed (byte* stringPointer = stringBytes)
        fixed (BmRow* rowPointer = rowArray)
        fixed (BmString* namePointer = nameArray)
        fixed (BmField* fieldPointer = fieldArray)
        fixed (BmString* optionPointer = optionArray)
        fixed (BmButton* buttonPointer = buttonArray)
        fixed (BmMenuItem* menuPointer = menuArray)
        fixed (BmTrayItem* trayPointer = trayArray)
        {
            var frame = screen;
            frame.Strings = stringPointer;
            frame.StringsLength = strings.Count;
            frame.Rows = rowPointer;
            frame.RowCount = rowArray.Length;
            frame.Names = namePointer;
            frame.Fields = fieldPointer;
            frame.FieldCount = fieldArray.Length;
            frame.Options = optionPointer;
            frame.OptionCount = optionArray.Length;
            frame.Buttons = buttonPointer;
            frame.ButtonCount = buttonArray.Length;
            frame.Menu = menuPointer;
            frame.MenuCount = menuArray.Length;
            frame.TrayItems = trayPointer;
            frame.TrayItemCount = trayArray.Length;
            return call(&frame);
        }
    }

    /// <summary>
    /// For the tests: the strings blob and the flattened tray, as text.
    /// </summary>
    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"page: {screen.Page} rows: {rows.Count} fields: {fields.Count} options: {options.Count} buttons: {buttons.Count} menu: {menu.Count} tray: {trayItems.Count} strings: {strings.Count} bytes");
        var blob = strings.ToArray();
        string Text(BmString value) => Encoding.UTF8.GetString(blob, value.Offset, value.Length);
        foreach (var row in rows)
        {
            builder.AppendLine($"row status={row.Status} flags={row.Flags} progress={row.Progress:0.00} '{Text(row.Name)}' '{Text(row.Detail)}' provider='{Text(row.Provider)}' '{Text(row.RunNumber)}''{Text(row.Timing)}' links='{Text(row.BuildLabel)}','{Text(row.BranchLabel)}','{Text(row.PullRequestLabel)}'");
        }

        foreach (var field in fields)
        {
            builder.AppendLine($"field kind={field.Kind} flags={field.Flags} '{Text(field.Id)}' '{Text(field.Label)}' '{Text(field.Value)}' options={field.OptionOffset}+{field.OptionCount}");
        }

        foreach (var button in buttons)
        {
            builder.AppendLine($"button flags={button.Flags} '{Text(button.Label)}'");
        }

        foreach (var item in trayItems)
        {
            builder.AppendLine($"tray depth={item.Depth} flags={item.Flags} '{Text(item.Id)}' '{Text(item.Label)}' icon='{Text(item.Icon)}'");
        }

        return builder.ToString();
    }
}
