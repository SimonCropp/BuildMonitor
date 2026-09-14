/// <summary>
/// The builds page, owner drawn: one row per <see cref="BuildRow"/> with a status square, a
/// provider icon, the names, a progress bar and chips for the links and actions. Hit rectangles are recorded as
/// the rows are drawn, so a click resolves against what was actually on screen.
/// </summary>
sealed class RowsCanvas : Control
{
    const int padding = 10;
    const int chipPadding = 8;
    const int iconSize = 16;
    const int spinnerSize = 18;
    const int minimumDetail = 120;

    BuildsPage? page;
    int menuShownForRow = -1;
    int hoverRow = -1;
    readonly List<(int Row, LinkKind Link, RowAction Action, Rectangle Bounds)> chips = [];
    readonly ContextMenuStrip contextMenu = new();
    Font bold;

    // Pending input, drained once per frame.
    int clickedRow = -1;
    int clickedLinkRow = -1;
    LinkKind clickedLink;
    int clickedActionRow = -1;
    RowAction clickedAction;
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
        else if (menuShownForRow != overlay.Row)
        {
            menuShownForRow = overlay.Row;
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

        var y = Math.Min(Height, (overlay.Row + 1) * RowHeight);
        contextMenu.Show(this, new(LogicalToDeviceUnits(padding + 20), y));
    }

    public MonitorInput Drain()
    {
        var input = new MonitorInput(
            Key: Key,
            ClickedRow: clickedRow,
            ClickedLinkRow: clickedLinkRow,
            ClickedLink: clickedLink,
            ClickedActionRow: clickedActionRow,
            ClickedAction: clickedAction,
            RightClickedRow: rightClickedRow,
            ClickedMenuItem: clickedMenuItem,
            MenuClosed: menuClosed,
            ScrollDelta: scrollDelta,
            ScrollTo: ScrollTo);
        Key = CommandKind.None;
        clickedRow = -1;
        clickedLinkRow = -1;
        clickedLink = LinkKind.None;
        clickedActionRow = -1;
        clickedAction = RowAction.None;
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
        var nameColumn = page.Names
            .Select(_ => MeasureName(_, Font))
            .Concat(page.GroupNames.Select(_ => MeasureName($"▾ {_}", bold)))
            .DefaultIfEmpty()
            .Max();
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

            DrawRow(graphics, row, bounds, index, iconWidth, nameColumn);
        }
    }

    static string DisplayName(BuildRow row) =>
        row.Kind == RowKind.Group ? $"{(row.Expanded ? "▾" : "▸")} {row.Name}" : row.Name;

    // With the padding Draw leaves, so the widest name fits without an ellipsis.
    static int MeasureName(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix).Width;

    Font NameFont(BuildRow row) =>
        row.Kind == RowKind.Group ? bold : Font;

    void DrawRow(Graphics graphics, BuildRow row, Rectangle bounds, int index, int iconWidth, int nameColumn)
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
        // Widths: the two names share what the fixed cells leave.
        var runWidth = LogicalToDeviceUnits(70);
        var barWidth = LogicalToDeviceUnits(110);
        var timingWidth = LogicalToDeviceUnits(90);
        var chipGap = LogicalToDeviceUnits(6);
        var chipInset = LogicalToDeviceUnits(chipPadding);
        // Reserved for the widest set of chips, so the columns line up whatever a row carries.
        var actionsWidth = Measure("Cancel") + 2 * chipInset + gap;
        var linksWidth = Measure("Build") + Measure("Branch") + Measure("PR 9999") + 3 * (2 * chipInset + chipGap);
        var fixedWidth = runWidth + barWidth + timingWidth + linksWidth + actionsWidth + 5 * gap;
        var names = Math.Max(LogicalToDeviceUnits(120), bounds.Width - x - fixedWidth);
        // As wide as the widest name, so a short project name leaves the pipeline room, but never
        // so wide that the pipeline cell drops below a readable width.
        var narrowest = LogicalToDeviceUnits(40);
        var nameWidth = Math.Clamp(nameColumn, narrowest, Math.Max(narrowest, names - LogicalToDeviceUnits(minimumDetail)));
        var detailWidth = names - nameWidth;

        Draw(graphics, DisplayName(row), NameFont(row), x, bounds, nameWidth, Palette.Text);
        x += nameWidth + gap;
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
            chips.Add((index, LinkKind.Project, RowAction.None, iconBounds));
        }

        Draw(graphics, row.Detail, Font, x + iconWidth, bounds, detailWidth - iconWidth, Palette.Dim);
        x += detailWidth + gap;
        Draw(graphics, row.RunNumber, Font, x, bounds, runWidth, Palette.Dim);
        x += runWidth + gap;

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

        foreach (var (kind, label) in Chips(row))
        {
            x = Chip(graphics, label, x, centreY, Palette.Chip, Palette.ChipText, index, kind, RowAction.None) + chipGap;
        }

        if (row.CanRetry)
        {
            x = Chip(graphics, "Retry", x, centreY, Palette.RetryChip, Palette.Text, index, LinkKind.None, RowAction.Retry) + chipGap;
        }

        if (row.CanCancel)
        {
            Chip(graphics, "Cancel", x, centreY, Palette.CancelChip, Palette.Text, index, LinkKind.None, RowAction.Cancel);
        }
    }

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

    static IEnumerable<(LinkKind Kind, string Label)> Chips(BuildRow row)
    {
        if (row.Build is not null)
        {
            yield return (LinkKind.Build, row.Build.Label);
        }

        if (row.Branch is not null)
        {
            yield return (LinkKind.Branch, row.Branch.Label);
        }

        if (row.PullRequest is not null)
        {
            yield return (LinkKind.PullRequest, row.PullRequest.Label);
        }
    }

    int Chip(Graphics graphics, string label, int x, int centreY, Color background, Color foreground, int row, LinkKind link, RowAction action)
    {
        var width = Measure(label) + 2 * LogicalToDeviceUnits(chipPadding);
        var bounds = new Rectangle(x, centreY - ChipHeight / 2, width, ChipHeight);
        using (var brush = new SolidBrush(background))
        using (var path = RoundedRectangle(bounds, LogicalToDeviceUnits(6)))
        {
            graphics.FillPath(brush, path);
        }

        TextRenderer.DrawText(graphics, label, Font, bounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        chips.Add((row, link, action, bounds));
        return bounds.Right;
    }

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
                if (chip.Link != LinkKind.None)
                {
                    clickedLinkRow = chip.Row;
                    clickedLink = chip.Link;
                }
                else
                {
                    clickedActionRow = chip.Row;
                    clickedAction = chip.Action;
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
