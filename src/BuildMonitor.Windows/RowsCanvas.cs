/// <summary>
/// The builds page, owner drawn: one row per <see cref="BuildRow"/> with a status square, a
/// provider icon, the names, a progress bar and chips for the links and actions. Hit rectangles are recorded as
/// the rows are drawn, so a click resolves against what was actually on screen.
/// </summary>
sealed class RowsCanvas : Control
{
    const int padding = 10;
    const int chipPadding = 8;
    const int chipSpacing = 6;
    const int iconSize = 16;
    const int spinnerSize = 18;
    const int barLength = 110;
    const int timingLength = 90;
    const int minimumDetail = 120;
    // Stands in for the chips a row has no room for, and opens the drop down that holds them.
    const string overflowLabel = "…";
    // The chips of the widest row, which the chips column is as wide as while there is room.
    static readonly string[] widestChips = ["Build", "Branch", "PR 9999", "Retry", "Copy log"];

    BuildsPage? page;
    int menuShownForRow = -1;
    bool menuShownOverflow;
    int hoverRow = -1;
    // Each clickable thing the last paint drew: a chip, the provider icon, or an overflow chip, which
    // carries the first of the chips it stands in for.
    readonly List<(int Row, ChipKind Chip, bool Overflow, Rectangle Bounds)> chips = [];
    readonly ContextMenuStrip contextMenu = new();
    Font bold;

    // Pending input, drained once per frame.
    int clickedRow = -1;
    int clickedChipRow = -1;
    ChipKind clickedChip;
    int clickedOverflowRow = -1;
    ChipKind overflowFrom;
    int rightClickedRow = -1;
    int clickedMenuItem = -1;
    bool menuClosed;
    int scrollDelta;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CommandKind Key { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ScrollTo { get; set; } = -1;

    public RowsCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        bold = new(Font, FontStyle.Bold);
        MenuTheme.Apply(contextMenu);
        contextMenu.ItemClicked += (_, arguments) =>
        {
            if (arguments.ClickedItem?.Tag is int index)
            {
                clickedMenuItem = index;
            }
        };
        contextMenu.Closed += (_, arguments) =>
        {
            // Forgotten however it closed, so a menu the session opens again for the same row, as
            // a second click on the chip that opened it does, is shown again rather than taken for
            // the one already up.
            menuShownForRow = -1;
            if (arguments.CloseReason != ToolStripDropDownCloseReason.ItemClicked)
            {
                menuClosed = true;
            }
        };
    }

    /// <summary>
    /// The canvas inherits the form's font only once it is parented, after the constructor, so a
    /// bold made there alone would keep the default size and draw group names smaller than rows.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        bold.Dispose();
        bold = new(Font, FontStyle.Bold);
    }

    /// <summary>
    /// Taken from the font's line height rather than fixed pixels. The app is per monitor DPI
    /// aware, so on a scaled display the font grows and a fixed chip would push its text out of
    /// the bottom.
    /// </summary>
    public int RowHeight => Font.Height + LogicalToDeviceUnits(14);

    int ChipHeight => Font.Height + LogicalToDeviceUnits(2);

    public int VisibleRows => Math.Max(1, Height / RowHeight);

    public void Apply(BuildsPage builds, MenuOverlay? overlay)
    {
        page = builds;
        if (overlay is null)
        {
            menuShownForRow = -1;
            // The session closes a menu whose row a poll moved. The strip is a window of its own,
            // not part of the frame, so it has to be closed too or it would stay up offering
            // items that do nothing.
            if (contextMenu.Visible)
            {
                contextMenu.Close();
            }
        }
        else if (menuShownForRow != overlay.Row ||
                 menuShownOverflow != overlay.Overflow)
        {
            menuShownForRow = overlay.Row;
            menuShownOverflow = overlay.Overflow;
            ShowMenu(overlay);
        }

        Invalidate();
    }

    public void Retheme()
    {
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
    }

    void ShowMenu(MenuOverlay overlay)
    {
        MenuTheme.Apply(contextMenu);
        contextMenu.Items.Clear();
        for (var index = 0; index < overlay.Labels.Count; index++)
        {
            contextMenu.Items.Add(new ToolStripMenuItem(overlay.Labels[index]) { Tag = index, ForeColor = Palette.Text });
        }

        // A drop down hangs under the overflow chip that opened it, a context menu under its row.
        var anchor = chips.LastOrDefault(_ => _.Overflow && _.Row == overlay.Row).Bounds;
        if (overlay.Overflow &&
            anchor != Rectangle.Empty)
        {
            contextMenu.Show(this, new(anchor.Left, anchor.Bottom));
            return;
        }

        var y = Math.Min(Height, (overlay.Row + 1) * RowHeight);
        contextMenu.Show(this, new(LogicalToDeviceUnits(padding + 20), y));
    }

    public MonitorInput Drain()
    {
        var input = new MonitorInput(
            Key: Key,
            ClickedRow: clickedRow,
            ClickedChipRow: clickedChipRow,
            ClickedChip: clickedChip,
            ClickedOverflowRow: clickedOverflowRow,
            OverflowFrom: overflowFrom,
            RightClickedRow: rightClickedRow,
            ClickedMenuItem: clickedMenuItem,
            MenuClosed: menuClosed,
            ScrollDelta: scrollDelta,
            ScrollTo: ScrollTo);
        Key = CommandKind.None;
        clickedRow = -1;
        clickedChipRow = -1;
        clickedChip = ChipKind.None;
        clickedOverflowRow = -1;
        overflowFrom = ChipKind.None;
        rightClickedRow = -1;
        clickedMenuItem = -1;
        menuClosed = false;
        scrollDelta = 0;
        ScrollTo = -1;
        return input;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Palette.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        chips.Clear();
        if (page is null)
        {
            return;
        }

        if (page.Rows.Count == 0)
        {
            var gap = LogicalToDeviceUnits(padding);
            var spinner = LogicalToDeviceUnits(spinnerSize);
            var x = gap;
            if (page.Loading)
            {
                DrawSpinner(graphics, new(x, gap + (RowHeight - spinner) / 2, spinner, spinner));
                x += spinner + gap;
            }

            TextRenderer.DrawText(graphics, page.Loading ? "Loading builds" : "Nothing to show yet.", Font, new Rectangle(x, gap, Width - x - gap, RowHeight), Palette.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        // Reserved on every row once any row has an icon, so a group's row, which has none, keeps
        // its name in line with the rows under it.
        var iconWidth = page.Rows.Any(_ => _.Provider.Length > 0) ? LogicalToDeviceUnits(iconSize + padding) : 0;
        var layout = ColumnWidths(page, iconWidth);
        for (var index = 0; index < page.Rows.Count; index++)
        {
            var top = index * RowHeight;
            if (top > Height)
            {
                break;
            }

            var row = page.Rows[index];
            var bounds = new Rectangle(0, top, Width, RowHeight);
            if (row.Selected)
            {
                graphics.FillRectangle(new SolidBrush(Palette.SelectedRow), bounds);
            }
            else if (index == hoverRow)
            {
                graphics.FillRectangle(new SolidBrush(Palette.HoverRow), bounds);
            }

            DrawRow(graphics, row, bounds, index, iconWidth, layout);
        }
    }

    /// <summary>
    /// The widths every row shares, so the columns line up. The name column is as wide as the
    /// widest name and the detail column as the widest pipeline and branch, up to a readable
    /// maximum, and the chips give way first: a row without room for all of them puts the last
    /// behind an overflow chip, where a fixed chips column cut the names short instead. Only once no
    /// chip but that one fits do the names shrink.
    /// </summary>
    (int Name, int Detail, int Chips) ColumnWidths(BuildsPage builds, int iconWidth)
    {
        var gap = LogicalToDeviceUnits(padding);
        // After the status square, a gap after each of the name, detail, bar, timing and chips.
        var available = Width - RowHeight - LogicalToDeviceUnits(barLength) - LogicalToDeviceUnits(timingLength) - 6 * gap;
        // With the padding Draw leaves, so the widest text fits without an ellipsis.
        var nameWanted = builds.Names
            .Select(_ => MeasureName(_, Font))
            .Concat(builds.GroupNames.Select(_ => MeasureName($"▾ {_}", bold)))
            .DefaultIfEmpty()
            .Max();
        // Forty characters at most: past that a long pipeline or branch is cut short rather than
        // pushing every row's chips into the drop down.
        var detailWanted = iconWidth + Math.Min(
            builds.Details.Select(_ => MeasureName(_, Font)).DefaultIfEmpty().Max(),
            MeasureName(new('0', 40), Font));
        var widest = widestChips.Sum(ChipWidth) + (widestChips.Length - 1) * LogicalToDeviceUnits(chipSpacing);
        var spare = available - nameWanted - detailWanted;
        var chipsWidth = spare >= widest ? widest : Math.Max(ChipWidth(overflowLabel), spare);
        var names = Math.Max(LogicalToDeviceUnits(120), available - chipsWidth);
        var narrowest = LogicalToDeviceUnits(40);
        var nameWidth = Math.Clamp(nameWanted, narrowest, Math.Max(narrowest, names - Math.Min(LogicalToDeviceUnits(minimumDetail), detailWanted)));
        return (nameWidth, names - nameWidth, chipsWidth);
    }

    static string DisplayName(BuildRow row) =>
        row.Kind == RowKind.Group ? $"{(row.Expanded ? "▾" : "▸")} {row.Name}" : row.Name;

    static int MeasureName(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix).Width;

    Font NameFont(BuildRow row) =>
        row.Kind == RowKind.Group ? bold : Font;

    void DrawRow(Graphics graphics, BuildRow row, Rectangle bounds, int index, int iconWidth, (int Name, int Detail, int Chips) layout)
    {
        // The full height of the row and flush with its neighbours, so a run of rows in one status
        // reads as one block rather than a column of dots.
        using (var brush = new SolidBrush(Palette.Status(row.Status)))
        {
            graphics.FillRectangle(brush, bounds.Left, bounds.Top, bounds.Height, bounds.Height);
        }

        var gap = LogicalToDeviceUnits(padding);
        var x = bounds.Height + gap;
        var centreY = bounds.Top + bounds.Height / 2;
        var barWidth = LogicalToDeviceUnits(barLength);
        var timingWidth = LogicalToDeviceUnits(timingLength);

        Draw(graphics, DisplayName(row), NameFont(row), x, bounds, layout.Name, Palette.Text);
        x += layout.Name + gap;
        // The logo leads the second cell, beside the pipeline it ran, so a group's members, whose
        // first cell is empty, still show which service each one came from.
        if (row.Provider.Length > 0 &&
            Icons.Glyph($"provider-{row.Provider}") is { } icon)
        {
            var side = LogicalToDeviceUnits(iconSize);
            var iconBounds = new Rectangle(x, centreY - side / 2, side, side);
            graphics.DrawImage(icon, iconBounds);
            // Hit tested like a chip, so the icon shows the hand and opens the project page
            // rather than selecting the row.
            chips.Add((index, ChipKind.Project, false, iconBounds));
        }

        Draw(graphics, row.Detail, Font, x + iconWidth, bounds, layout.Detail - iconWidth, Palette.Dim);
        x += layout.Detail + gap;

        if (row.Progress >= 0)
        {
            var trackHeight = LogicalToDeviceUnits(8);
            var track = new Rectangle(x, centreY - trackHeight / 2, barWidth, trackHeight);
            using var trackBrush = new SolidBrush(Palette.BarTrack);
            graphics.FillRectangle(trackBrush, track);
            using var fillBrush = new SolidBrush(Palette.Status(BuildStatus.Running));
            graphics.FillRectangle(fillBrush, new(track.Left, track.Top, (int) (track.Width * row.Progress), track.Height));
        }

        x += barWidth + gap;
        Draw(graphics, row.Timing, Font, x, bounds, timingWidth, Palette.Dim);
        x += timingWidth + gap;
        DrawChips(graphics, row, index, x, x + layout.Chips, centreY);
    }

    /// <summary>
    /// The chips that fit, from the left, then an overflow chip in place of the rest. A chip is
    /// drawn only with room after it for the overflow chip, unless it is the last, so the overflow
    /// chip always fits where the first chip that did not would have gone.
    /// </summary>
    void DrawChips(Graphics graphics, BuildRow row, int index, int x, int right, int centreY)
    {
        var chipGap = LogicalToDeviceUnits(chipSpacing);
        var overflowWidth = ChipWidth(overflowLabel);
        for (var position = 0; position < row.Chips.Count; position++)
        {
            var chip = row.Chips[position];
            var reserve = position == row.Chips.Count - 1 ? 0 : chipGap + overflowWidth;
            if (x + ChipWidth(chip.Label) + reserve > right)
            {
                Chip(graphics, overflowLabel, x, centreY, Palette.Chip, Palette.Text, index, chip.Kind, overflow: true);
                return;
            }

            var (background, foreground) = Colours(chip.Kind);
            x = Chip(graphics, chip.Label, x, centreY, background, foreground, index, chip.Kind, overflow: false) + chipGap;
        }
    }

    static (Color Background, Color Foreground) Colours(ChipKind kind) =>
        kind switch
        {
            ChipKind.Retry => (Palette.RetryChip, Palette.Text),
            ChipKind.Cancel => (Palette.CancelChip, Palette.Text),
            ChipKind.CopyLog => (Palette.Chip, Palette.Text),
            _ => (Palette.Chip, Palette.ChipText)
        };

    /// <summary>
    /// An arc turning once a second, driven by the clock rather than a timer: the frame loop
    /// already repaints the canvas every frame.
    /// </summary>
    static void DrawSpinner(Graphics graphics, Rectangle bounds)
    {
        var angle = Environment.TickCount64 % 1000 * 360f / 1000;
        using var pen = new Pen(Palette.Dim, 2.5f);
        graphics.DrawArc(pen, bounds, angle, 270);
    }

    int Chip(Graphics graphics, string label, int x, int centreY, Color background, Color foreground, int row, ChipKind kind, bool overflow)
    {
        var bounds = new Rectangle(x, centreY - ChipHeight / 2, ChipWidth(label), ChipHeight);
        using (var brush = new SolidBrush(background))
        using (var path = RoundedRectangle(bounds, LogicalToDeviceUnits(6)))
        {
            graphics.FillPath(brush, path);
        }

        TextRenderer.DrawText(graphics, label, Font, bounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        chips.Add((row, kind, overflow, bounds));
        return bounds.Right;
    }

    int ChipWidth(string label) =>
        Measure(label) + 2 * LogicalToDeviceUnits(chipPadding);

    static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    int Measure(string text) =>
        text.Length == 0 ? 0 : TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;

    static void Draw(Graphics graphics, string text, Font font, int x, Rectangle bounds, int width, Color colour) =>
        TextRenderer.DrawText(graphics, text, font, new Rectangle(x, bounds.Top, width, bounds.Height), colour, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    int RowAt(int y)
    {
        if (page is null)
        {
            return -1;
        }

        var row = y / RowHeight;
        return row >= 0 && row < page.Rows.Count ? row : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = RowAt(e.Y);
        Cursor = chips.Any(_ => _.Bounds.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        if (row != hoverRow)
        {
            hoverRow = row;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverRow = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var row = RowAt(e.Y);
        if (e.Button == MouseButtons.Right)
        {
            rightClickedRow = row;
        }
        else if (e.Button == MouseButtons.Left)
        {
            var chip = chips.FirstOrDefault(_ => _.Bounds.Contains(e.Location));
            if (chip.Bounds != Rectangle.Empty)
            {
                if (chip.Overflow)
                {
                    clickedOverflowRow = chip.Row;
                    overflowFrom = chip.Chip;
                }
                else
                {
                    clickedChipRow = chip.Row;
                    clickedChip = chip.Chip;
                }
            }
            else if (e.Clicks < 2 ||
                     !IsGroup(row))
            {
                // The second press of a double click on a group is dropped: the first already
                // toggled it, and a second toggle would close what was just opened.
                clickedRow = row;
            }
        }

        base.OnMouseDown(e);
    }

    bool IsGroup(int row) =>
        row >= 0 &&
        page is not null &&
        page.Rows[row].Kind == RowKind.Group;

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        var row = RowAt(e.Y);
        if (e.Button == MouseButtons.Left &&
            row >= 0 &&
            !IsGroup(row) &&
            !chips.Any(_ => _.Bounds.Contains(e.Location)))
        {
            Key = CommandKind.OpenBuild;
        }

        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scrollDelta -= e.Delta / 40;
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Enter || base.IsInputKey(keyData);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            contextMenu.Dispose();
            bold.Dispose();
        }

        base.Dispose(disposing);
    }
}
