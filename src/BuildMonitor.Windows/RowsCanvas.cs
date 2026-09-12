/// <summary>
/// The builds page, owner drawn: one row per <see cref="BuildRow"/> with a status dot, the
/// names, a progress bar and chips for the links and actions. Hit rectangles are recorded as
/// the rows are drawn, so a click resolves against what was actually on screen.
/// </summary>
sealed class RowsCanvas : Control
{
    public const int RowHeight = 30;
    const int padding = 10;
    const int chipPadding = 8;
    const int chipHeight = 20;

    BuildsPage? page;
    MenuOverlay? menu;
    int menuShownForRow = -1;
    int hoverRow = -1;
    readonly List<(int Row, LinkKind Link, RowAction Action, Rectangle Bounds)> chips = [];
    readonly ContextMenuStrip contextMenu = new();
    readonly ToolTip toolTip = new();
    string? tooltipText;
    readonly Font bold;

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

    public int VisibleRows => Math.Max(1, Height / RowHeight);

    public void Apply(BuildsPage builds, MenuOverlay? overlay)
    {
        page = builds;
        menu = overlay;
        if (overlay is null)
        {
            menuShownForRow = -1;
        }
        else if (menuShownForRow != overlay.Row)
        {
            menuShownForRow = overlay.Row;
            ShowMenu(overlay);
        }

        Invalidate();
    }

    void ShowMenu(MenuOverlay overlay)
    {
        contextMenu.Items.Clear();
        for (var index = 0; index < overlay.Labels.Count; index++)
        {
            contextMenu.Items.Add(new ToolStripMenuItem(overlay.Labels[index]) { Tag = index, ForeColor = Palette.Text });
        }

        var y = Math.Min(Height, (overlay.Row + 1) * RowHeight);
        contextMenu.Show(this, new Point(padding + 20, y));
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
            TextRenderer.DrawText(graphics, "Nothing to show yet.", Font, new Rectangle(padding, padding, Width - 2 * padding, RowHeight), Palette.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

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
            else if (row.Kind == RowKind.Header)
            {
                graphics.FillRectangle(new SolidBrush(Palette.HeaderRow), bounds);
            }

            if (row.Kind == RowKind.Header)
            {
                DrawHeader(graphics, row, bounds);
            }
            else
            {
                DrawBuild(graphics, row, bounds, index);
            }
        }
    }

    void DrawHeader(Graphics graphics, BuildRow row, Rectangle bounds)
    {
        var marker = row.Folded ? "▸" : "▾";
        TextRenderer.DrawText(graphics, $"{marker} {row.Pipeline}", bold, new Rectangle(padding, bounds.Top, bounds.Width - 2 * padding, bounds.Height), Palette.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void DrawBuild(Graphics graphics, BuildRow row, Rectangle bounds, int index)
    {
        var x = padding + 12;
        var centreY = bounds.Top + bounds.Height / 2;
        using (var brush = new SolidBrush(Palette.Status(row.Status)))
        {
            graphics.FillEllipse(brush, x, centreY - 5, 10, 10);
        }

        x += 22;
        // Widths: the two names share what the fixed cells leave.
        const int runWidth = 60;
        const int statusWidth = 80;
        const int barWidth = 110;
        const int timingWidth = 80;
        // Reserved for the widest set of chips, so the columns line up whatever a row carries.
        var actionsWidth = Measure("Cancel") + 2 * chipPadding + padding;
        var linksWidth = Measure("Build") + Measure("Branch") + Measure("PR 9999") + 3 * (2 * chipPadding + 6);
        var fixedWidth = runWidth + statusWidth + barWidth + timingWidth + linksWidth + actionsWidth + 6 * padding;
        var names = Math.Max(120, bounds.Width - x - fixedWidth);
        var pipelineWidth = names * 9 / 20;
        var repoWidth = names - pipelineWidth;

        Draw(graphics, row.Pipeline, Font, x, bounds, pipelineWidth, Palette.Text);
        x += pipelineWidth + padding;
        Draw(graphics, row.RepoBranch, Font, x, bounds, repoWidth, Palette.Dim);
        x += repoWidth + padding;
        Draw(graphics, row.RunNumber, Font, x, bounds, runWidth, Palette.Dim);
        x += runWidth + padding;
        Draw(graphics, row.StatusText, Font, x, bounds, statusWidth, Palette.Status(row.Status));
        x += statusWidth + padding;

        if (row.Progress >= 0)
        {
            var track = new Rectangle(x, centreY - 4, barWidth, 8);
            using var trackBrush = new SolidBrush(Palette.BarTrack);
            graphics.FillRectangle(trackBrush, track);
            using var fillBrush = new SolidBrush(Palette.Status(BuildStatus.Running));
            graphics.FillRectangle(fillBrush, new Rectangle(track.Left, track.Top, (int) (track.Width * row.Progress), track.Height));
        }

        x += barWidth + padding;
        Draw(graphics, row.Timing, Font, x, bounds, timingWidth, Palette.Dim);
        x += timingWidth + padding;

        foreach (var (kind, label) in Chips(row))
        {
            x = Chip(graphics, label, x, centreY, Palette.Chip, Palette.ChipText, index, kind, RowAction.None) + 6;
        }

        if (row.CanRetry)
        {
            x = Chip(graphics, "Retry", x, centreY, Palette.RetryChip, Palette.Text, index, LinkKind.None, RowAction.Retry) + 6;
        }

        if (row.CanCancel)
        {
            Chip(graphics, "Cancel", x, centreY, Palette.CancelChip, Palette.Text, index, LinkKind.None, RowAction.Cancel);
        }
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
        var width = Measure(label) + 2 * chipPadding;
        var bounds = new Rectangle(x, centreY - chipHeight / 2, width, chipHeight);
        using (var brush = new SolidBrush(background))
        using (var path = RoundedRectangle(bounds, 6))
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
            var text = row >= 0 && page is not null ? page.Rows[row].Tooltip : "";
            if (text != tooltipText)
            {
                tooltipText = text;
                toolTip.SetToolTip(this, text);
            }

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
            else
            {
                clickedRow = row;
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left &&
            RowAt(e.Y) >= 0 &&
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
            toolTip.Dispose();
            bold.Dispose();
        }

        base.Dispose(disposing);
    }
}
