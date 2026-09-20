/// <summary>
/// The builds page, owner drawn: one row per <see cref="BuildRow"/> with a status square, a
/// provider icon, the names with the run and branch as links, a progress bar and chips for the pull
/// request and the actions. Hit rectangles are recorded as the rows are drawn, so a click resolves
/// against what was actually on screen.
/// </summary>
sealed class RowsCanvas : Control
{
    const int padding = 10;
    const int chipPadding = 8;
    const int chipSpacing = 6;
    // The icon inside a chip, which is a pill the height of a line of text.
    const int iconSize = 16;
    // How far short of the row's height a logo is drawn: enough that the logos of the rows above
    // and below do not touch, and no more. The marks carry detail, a Jenkins butler's face and the
    // play badge on the Actions mark, that a sixteen pixel square turned to a smudge.
    const int logoInset = 6;
    // The glyph a logo is scaled from: the larger of the two written, scaled down, where a chip's
    // icon is drawn at the size it was written. Scaled up from the smaller a logo came out soft.
    const int logoSource = 32;
    const int spinnerSize = 18;
    const int barLength = 110;
    // A floor rather than the width: the column is as wide as the widest timing text in the font
    // actually in use, since a fixed length that fit at the default size clipped "queued 30s" once
    // the text was scaled up.
    const int minimumTiming = 90;
    const int minimumDetail = 120;
    // How long the pointer rests on one part of a row before that part says what it is. Long
    // enough that crossing a row on the way somewhere else says nothing at all.
    const int tipDelay = 1200;
    // How long it then stays: enough to read a row's whole summary, which runs to four lines.
    const int tipDuration = 20000;
    // Clear of the pointer, so the text is not under the hand that asked for it.
    const int tipOffset = 18;
    // Without padding, so each run of the detail starts where the text before it ended.
    const TextFormatFlags runFlags = TextFormatFlags.Left |
                                     TextFormatFlags.VerticalCenter |
                                     TextFormatFlags.EndEllipsis |
                                     TextFormatFlags.NoPrefix |
                                     TextFormatFlags.NoPadding;
    // Stands in for the chips a row has no room for, and opens the drop down that holds them.
    const string overflowLabel = "…";
    // Between a chip's icon and the text after it, where it has both.
    const int chipIconGap = 4;
    // The chips of the widest row, which the chips column is as wide as while there is room: a
    // failed pull request build with a checkout carries every one of them. Cancel and Run next are
    // not among them; both belong to a build still going or still waiting, which has neither a log
    // to copy nor anything to triage, so that row is the narrower of the two whatever it carries.
    static (string Icon, string Text)[] widestChips =
    [
        ("pull-request", "9999"),
        ("retry", ""),
        ("log", ""),
        ("folder", ""),
        ("triage", "")
    ];

    BuildsPage? page;
    int menuShownForRow = -1;
    bool menuShownOverflow;
    int hoverRow = -1;
    // The link in the text under the pointer, underlined so it reads as a link before it is clicked.
    Rectangle hoverLink = Rectangle.Empty;
    // Each clickable thing the last paint drew: a chip, a link in the text, the status square, the
    // provider icon, or an overflow chip, which carries the first of the chips it stands in for.
    List<(int Row, ChipKind Chip, bool Overflow, Rectangle Bounds)> chips = [];
    // Each hover text the last paint drew, in the order drawn, so a later one wins where two
    // overlap: a cell's own text over the one for the whole row.
    List<(Rectangle Bounds, string Text)> tips = [];
    // Shown by hand rather than by assigning the control a tool and letting it decide when:
    // InitialDelay only governs the first time the pointer enters a tool, and the whole canvas is
    // one tool whose text changes as the pointer crosses cells, so the control re-showed instantly
    // over every cell the pointer passed however long the delays were set to.
    ToolTip toolTip = new()
    {
        ShowAlways = true
    };
    System.Windows.Forms.Timer tipTimer = new()
    {
        Interval = tipDelay
    };
    // What is waiting on the timer, or showing. Compared to decide whether a move is onto something
    // new: within one cell the pointer must be free to drift without restarting the wait.
    string tipPending = "";
    Point tipAt;
    bool tipVisible;
    ContextMenuStrip contextMenu = new();
    Font bold;
    Font underline;
    // A group's name is bold, and now also a link, so hovering it needs both at once.
    Font boldUnderline;
    // Text widths by the text, the font's style and the flags. Measuring was most of a paint: the
    // columns measure every name, detail and author across all rows, and each visible row measured
    // its runs and chips twice, about five hundred strings and 26 ms on a large account.
    Dictionary<(string Text, FontStyle Style, TextFormatFlags Flags), int> widths = new();

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
        underline = new(Font, FontStyle.Underline);
        boldUnderline = new(Font, FontStyle.Bold | FontStyle.Underline);
        tipTimer.Tick += (_, _) => ShowPendingTip();
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
    /// The widths go for the same reason: kept, they would size the columns for the default font,
    /// and after a move to a scaled display, for the display before.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        widths.Clear();
        bold.Dispose();
        bold = new(Font, FontStyle.Bold);
        underline.Dispose();
        underline = new(Font, FontStyle.Underline);
        boldUnderline.Dispose();
        boldUnderline = new(Font, FontStyle.Bold | FontStyle.Underline);
    }

    /// <summary>
    /// Taken from the font's line height rather than fixed pixels. The app is per monitor DPI
    /// aware, so on a scaled display the font grows and a fixed chip would push its text out of
    /// the bottom.
    /// </summary>
    public int RowHeight => Font.Height + LogicalToDeviceUnits(14);

    /// <summary>
    /// The square a row's two marks are drawn in: the host's before the name, and the provider's
    /// before the pipeline. Taken from the row for the same reason its height is, so a scaled
    /// display grows the pictures with the text rather than leaving them in a corner of the row.
    /// </summary>
    int LogoSize => RowHeight - LogicalToDeviceUnits(logoInset);

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
            contextMenu.Items.Add(
                new ToolStripMenuItem(overlay.Labels[index])
                {
                    Tag = index,
                    ForeColor = Palette.Text
                });
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
        // The logos are scaled from a glyph larger than the square they are drawn in, and the
        // default filter left the small detail in one, a play badge or a face, muddy.
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        chips.Clear();
        tips.Clear();
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

            TextRenderer.DrawText(graphics, page.Empty, Font, new Rectangle(x, gap, Width - x - gap, RowHeight), Palette.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        // Reserved on every row once any row has an icon, so a group's row, which has none, keeps
        // its name in line with the rows under it. The same for the host's mark before the name,
        // which a row whose provider gave no repository URL does not have.
        var logo = LogoSize + LogicalToDeviceUnits(padding);
        var iconWidth = page.Rows.Any(_ => _.DetailIcon.Length > 0) ? logo : 0;
        var markWidth = page.Rows.Any(_ => _.NameIcon.Length > 0) ? logo : 0;
        // A detail's runs are measured as the text so far, which differs by row, so the widths would
        // grow without end as builds come and go. Emptied once they hold several times what the
        // columns measure, which leaves room for every row's runs, and here rather than as they
        // fill, so a paint never throws away a width it is about to read again.
        var measured = page.Names.Count + page.GroupNames.Count + page.Details.Count + (page.Authors?.Count ?? 0);
        if (widths.Count > 4 * measured + 1000)
        {
            widths.Clear();
        }

        var layout = ColumnWidths(page, iconWidth, markWidth);
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
                using var brush = new SolidBrush(Palette.SelectedRow);
                graphics.FillRectangle(brush, bounds);
            }
            else if (index == hoverRow)
            {
                using var brush = new SolidBrush(Palette.HoverRow);
                graphics.FillRectangle(brush, bounds);
            }

            DrawRow(graphics, row, bounds, index, iconWidth, markWidth, layout);
        }

        // A poll moves the rows under a pointer that has not moved, and a tooltip stays up for
        // twenty seconds, so without this one could sit there describing the row that used to be
        // under it. Only where the pointer is already hovering something: a repaint is not itself
        // a reason to start showing one.
        if (tipPending.Length > 0)
        {
            ShowTip(PointToClient(MousePosition));
        }
    }

    /// <summary>
    /// The widths every row shares, so the columns line up. The name column is as wide as the
    /// widest name and the detail column as the widest pipeline and branch, up to a readable
    /// maximum. The bar gives way before anything else, since the timing beside it says the same:
    /// it shows only while the names, the detail and every chip still fit. Then the chips: a row
    /// without room for all of them puts the last behind an overflow chip, where a fixed chips column
    /// cut the names short instead. Only once no chip but that one fits do the names shrink.
    /// </summary>
    (int Name, int Detail, int Bar, int Author, int Chips) ColumnWidths(BuildsPage builds, int iconWidth, int markWidth)
    {
        var gap = LogicalToDeviceUnits(padding);
        // As wide as the widest name shown, up to twenty characters, and gone with its gap when no
        // failed build names anyone.
        var authorWidth = Math.Min(
            (builds.Authors ?? []).Select(_ => MeasureName(_, Font)).DefaultIfEmpty().Max(),
            MeasureName(new('0', 20), Font));
        // After the status square, a gap after each of the name, detail, timing and chips, and the
        // author and its gap when shown.
        var available = Width - RowHeight - TimingWidth() - 5 * gap - (authorWidth > 0 ? authorWidth + gap : 0);
        // With the padding Draw leaves, so the widest text fits without an ellipsis.
        var nameWanted = markWidth + builds.Names
            .Select(_ => MeasureName(_, Font))
            .Concat(builds.GroupNames.Select(_ => MeasureName($"▼ {_}", bold)))
            .DefaultIfEmpty()
            .Max();
        // Forty characters at most: past that a long pipeline or branch is cut short rather than
        // pushing every row's chips into the drop down.
        var detailWanted = iconWidth + Math.Min(
            builds.Details.Select(_ => MeasureName(_, Font)).DefaultIfEmpty().Max(),
            MeasureName(new('0', 40), Font));
        var widest = WidestChips();
        var bar = LogicalToDeviceUnits(barLength);
        var barWidth = available - bar - gap - nameWanted - detailWanted >= widest ? bar : 0;
        if (barWidth > 0)
        {
            available -= bar + gap;
        }

        var spare = available - nameWanted - detailWanted;
        var chipsWidth = spare >= widest ? widest : Math.Max(ChipWidth("", overflowLabel), spare);
        var names = Math.Max(LogicalToDeviceUnits(120), available - chipsWidth);
        var narrowest = LogicalToDeviceUnits(40);
        var nameWidth = Math.Clamp(nameWanted, narrowest, Math.Max(narrowest, names - Math.Min(LogicalToDeviceUnits(minimumDetail), detailWanted)));
        return (nameWidth, names - nameWidth, barWidth, authorWidth, chipsWidth);
    }

    /// <summary>
    /// The open or closed arrow a group's row is drawn behind, and nothing for any other row.
    /// </summary>
    static string GroupArrow(BuildRow row)
    {
        if (row.Kind == RowKind.Group)
        {
            return row.Expanded ? "▼ " : "▶ ";
        }

        return "";
    }

    int MeasureName(string text, Font font) =>
        TextWidth(text, font, TextFormatFlags.NoPrefix);

    int TimingWidth() =>
        Math.Max(
            LogicalToDeviceUnits(minimumTiming),
            Progress.Widest.Select(_ => MeasureName(_, Font)).Max());

    Font NameFont(BuildRow row)
    {
        if (row.Kind == RowKind.Group)
        {
            return bold;
        }

        return Font;
    }

    void DrawRow(Graphics graphics, BuildRow row, Rectangle bounds, int index, int iconWidth, int markWidth, (int Name, int Detail, int Bar, int Author, int Chips) layout)
    {
        // First, so every cell drawn after it covers it where that cell has something of its own.
        Tip(bounds, row.Tooltip(RowPart.Row));
        // The full height of the row and flush with its neighbours, so a run of rows in one status
        // reads as one block rather than a column of dots.
        var square = new Rectangle(bounds.Left, bounds.Top, bounds.Height, bounds.Height);
        using (var brush = new SolidBrush(Palette.Status(row.Status)))
        {
            graphics.FillRectangle(brush, square);
        }

        // The square opens the run. It is the one cell every build row has: where the pipeline is
        // named after the project the second cell leaves it out, and a group's member has no first
        // cell either, so without this such a row named its run nowhere a click could reach.
        if (row.StatusLink != ChipKind.None)
        {
            chips.Add((index, row.StatusLink, false, square));
            Tip(square, row.Tooltip(RowPart.Status));
        }

        var gap = LogicalToDeviceUnits(padding);
        var x = bounds.Height + gap;
        var centreY = bounds.Top + bounds.Height / 2;
        var timingWidth = TimingWidth();

        DrawName(graphics, row, index, x, bounds, layout.Name, markWidth);
        x += layout.Name + gap;
        // The second of the row's two marks leads the second cell, beside the pipeline, so a
        // group's members, whose first cell is empty, still show which service each one came from.
        if (row.DetailIcon.Length > 0)
        {
            var side = LogoSize;
            var iconBounds = new Rectangle(x, centreY - side / 2, side, side);
            // Hit tested like a chip, so the mark shows the hand and opens what it stands for
            // rather than selecting the row. Only where one was drawn: a picture that is not there
            // is not something to aim at.
            if (Icons.Draw(graphics, row.DetailIcon, iconBounds, logoSource))
            {
                chips.Add((index, row.DetailIconLink, false, iconBounds));
                Tip(iconBounds, row.Tooltip(RowPart.DetailIcon));
            }
        }

        DrawDetail(graphics, row, index, x + iconWidth, bounds, layout.Detail - iconWidth);
        x += layout.Detail + gap;

        if (layout.Bar > 0)
        {
            if (row.Progress >= 0)
            {
                var trackHeight = LogicalToDeviceUnits(8);
                var track = new Rectangle(x, centreY - trackHeight / 2, layout.Bar, trackHeight);
                using var trackBrush = new SolidBrush(Palette.BarTrack);
                graphics.FillRectangle(trackBrush, track);
                using var fillBrush = new SolidBrush(Palette.Status(BuildStatus.Running));
                graphics.FillRectangle(fillBrush, new(track.Left, track.Top, (int) (track.Width * row.Progress), track.Height));
            }

            x += layout.Bar + gap;
        }

        Draw(graphics, row.Timing, Font, x, bounds, timingWidth, Palette.Dim);
        Tip(new(x, bounds.Top, timingWidth, bounds.Height), row.Tooltip(RowPart.Timing));
        x += timingWidth + gap;
        if (layout.Author > 0)
        {
            Draw(graphics, row.Author, Font, x, bounds, layout.Author, Palette.Text);
            x += layout.Author + gap;
        }

        DrawChips(graphics, row, index, x, x + layout.Chips, centreY);
    }

    /// <summary>
    /// The first cell, in the link colour where the name opens the run. Its hit rectangle is the text
    /// as drawn rather than the cell, so a click beside a short name still selects the row.
    /// </summary>
    void DrawName(Graphics graphics, BuildRow row, int index, int x, Rectangle bounds, int width, int markWidth)
    {
        // Through NameFont, so a group's name keeps the weight that makes it read as a heading
        // while it takes the link colour, and the hit rectangle is measured in the font drawn.
        var font = NameFont(row);
        // The arrow is drawn before the name but is no part of it: it is what opens and closes the
        // group, so it stays in the ordinary colour and outside the link's rectangle, and a click
        // on it reaches the row. Inside the link it left a closed group with no way to expand.
        var arrow = GroupArrow(row);
        if (arrow.Length > 0)
        {
            Draw(graphics, arrow, font, x, bounds, width, Palette.Text);
            var arrowWidth = Math.Min(MeasureName(arrow, font), width);
            x += arrowWidth;
            width -= arrowWidth;
        }

        // The host's mark leads the name, in a width reserved on every row once any row has one,
        // so the names still line up where a provider gave no repository URL to read a host from.
        var markLeft = x;
        if (markWidth > 0)
        {
            if (row.NameIcon.Length > 0)
            {
                var side = LogoSize;
                Icons.Draw(graphics, row.NameIcon, new(x, bounds.Top + (bounds.Height - side) / 2, side, side), logoSource);
            }

            x += markWidth;
            width -= markWidth;
        }

        if (row.NameLink == ChipKind.None)
        {
            Draw(graphics, row.Name, font, x, bounds, width, Palette.Text);
            return;
        }

        var link = LinkBounds(x, Math.Min(MeasureName(row.Name, font), width), bounds);
        if (row.NameIcon.Length > 0)
        {
            // The mark opens what the name does, so the two are one target rather than a link with
            // a picture beside it that does nothing. As tall as the mark, which stands above and
            // below a line of text.
            link = new(markLeft, bounds.Top + (bounds.Height - LogoSize) / 2, link.Right - markLeft, LogoSize);
        }

        Draw(graphics, row.Name, link == hoverLink ? Hovered(font) : font, x, bounds, width, Palette.ChipText);
        chips.Add((index, row.NameLink, false, link));
        Tip(link, row.Tooltip(RowPart.Name));
    }

    /// <summary>
    /// The same font underlined, which is how a link says it is one before it is clicked.
    /// </summary>
    Font Hovered(Font font)
    {
        if (font == bold)
        {
            return boldUnderline;
        }

        return underline;
    }

    /// <summary>
    /// The second cell run by run: plain text dimmed and links in the link colour, cut short with an
    /// ellipsis where the cell ends. Each run starts at the measured width of all the text before it
    /// rather than the sum of each run's own width, which would round short a pixel a run, and each
    /// link's hit rectangle is clipped to what showed of it.
    /// </summary>
    void DrawDetail(Graphics graphics, BuildRow row, int index, int x, Rectangle bounds, int width)
    {
        var right = x + width;
        var before = "";
        foreach (var span in row.Detail)
        {
            var left = x + Measure(before);
            if (left >= right)
            {
                return;
            }

            before += span.Text;
            var cell = new Rectangle(left, bounds.Top, right - left, bounds.Height);
            if (span.Link == ChipKind.None)
            {
                TextRenderer.DrawText(graphics, span.Text, Font, cell, Palette.Dim, runFlags);
                continue;
            }

            var link = LinkBounds(left, Math.Min(x + Measure(before), right) - left, bounds);
            TextRenderer.DrawText(graphics, span.Text, link == hoverLink ? underline : Font, cell, Palette.ChipText, runFlags);
            chips.Add((index, span.Link, false, link));
            Tip(link, row.Tooltip(span.Link == ChipKind.Branch ? RowPart.Branch : RowPart.Pipeline));
        }
    }

    /// <summary>
    /// A line of text centred in its row: what a link in the text is hit tested against.
    /// </summary>
    Rectangle LinkBounds(int x, int width, Rectangle row) =>
        new(x, row.Top + (row.Height - Font.Height) / 2, width, Font.Height);

    /// <summary>
    /// The chips that fit, from the left, then an overflow chip in place of the rest. A chip is
    /// drawn only with room after it for the overflow chip, unless it is the last, so the overflow
    /// chip always fits where the first chip that did not would have gone.
    /// </summary>
    void DrawChips(Graphics graphics, BuildRow row, int index, int x, int right, int centreY)
    {
        var chipGap = LogicalToDeviceUnits(chipSpacing);
        var overflowWidth = ChipWidth("", overflowLabel);
        for (var position = 0; position < row.Chips.Count; position++)
        {
            var chip = row.Chips[position];
            var reserve = position == row.Chips.Count - 1 ? 0 : chipGap + overflowWidth;
            if (x + ChipWidth(chip) + reserve > right)
            {
                // Which chips it stands in for depends on the width this row was given, so the one
                // thing it can say is that there are more, as the … itself does.
                Chip(graphics, "", overflowLabel, x, centreY, Palette.Chip, Palette.Text, index, chip.Kind, overflow: true, "More actions");
                return;
            }

            var (background, foreground) = Colours(chip.Kind);
            x = Chip(graphics, chip.Icon, chip.Text, x, centreY, background, foreground, index, chip.Kind, overflow: false, chip.Tooltip) + chipGap;
        }
    }

    static (Color Background, Color Foreground) Colours(ChipKind kind) =>
        kind switch
        {
            ChipKind.Retry => (Palette.RetryChip, Palette.Text),
            ChipKind.Cancel => (Palette.CancelChip, Palette.Text),
            ChipKind.CopyLog => (Palette.Chip, Palette.Text),
            ChipKind.Triage => (Palette.Chip, Palette.Text),
            _ => (Palette.Chip, Palette.ChipText)
        };

    /// <summary>
    /// An arc turning once a second, driven by the clock rather than a timer: while the page is
    /// loading, the screen is rebuilt, and the canvas repainted, four times a second.
    /// </summary>
    static void DrawSpinner(Graphics graphics, Rectangle bounds)
    {
        var angle = Environment.TickCount64 % 1000 * 360f / 1000;
        using var pen = new Pen(Palette.Dim, 2.5f);
        graphics.DrawArc(pen, bounds, angle, 270);
    }

    /// <summary>
    /// One pill: its icon, then its text, either of which may be empty. The icon stands where the
    /// text would start, so a row of chips keeps one rhythm whichever of the two each one carries.
    /// </summary>
    int Chip(Graphics graphics, string icon, string text, int x, int centreY, Color background, Color foreground, int row, ChipKind kind, bool overflow, string tooltip)
    {
        var bounds = new Rectangle(x, centreY - ChipHeight / 2, ChipWidth(icon, text), ChipHeight);
        using (var brush = new SolidBrush(background))
        using (var path = RoundedRectangle(bounds, LogicalToDeviceUnits(6)))
        {
            graphics.FillPath(brush, path);
        }

        var left = bounds.Left + LogicalToDeviceUnits(chipPadding);
        if (icon.Length > 0)
        {
            // A chip whose glyph is missing is still drawn and still clickable: an empty pill is
            // odd, but one that vanished because IconBuilder never ran would be worse.
            var side = LogicalToDeviceUnits(iconSize);
            Icons.Draw(graphics, icon, new(left, bounds.Top + (bounds.Height - side) / 2, side, side));
            left += side;
            if (text.Length > 0)
            {
                left += LogicalToDeviceUnits(chipIconGap);
            }
        }

        if (text.Length > 0)
        {
            const TextFormatFlags textFormatFlags = TextFormatFlags.Left |
                                                    TextFormatFlags.VerticalCenter |
                                                    TextFormatFlags.NoPadding;
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(left, bounds.Top, bounds.Right - left, bounds.Height), foreground, textFormatFlags);
        }

        chips.Add((row, kind, overflow, bounds));
        Tip(bounds, tooltip);
        return bounds.Right;
    }

    int ChipWidth(string icon, string text)
    {
        var width = 2 * LogicalToDeviceUnits(chipPadding);
        if (icon.Length > 0)
        {
            width += LogicalToDeviceUnits(iconSize);
        }

        if (text.Length > 0)
        {
            width += Measure(text);
        }

        if (icon.Length > 0 &&
            text.Length > 0)
        {
            width += LogicalToDeviceUnits(chipIconGap);
        }

        return width;
    }

    int ChipWidth(RowChip chip) =>
        ChipWidth(chip.Icon, chip.Text);

    /// <summary>
    /// The whole chips column at its widest: every chip the widest row can carry, with a gap
    /// between each.
    /// </summary>
    int WidestChips() =>
        widestChips.Sum(_ => ChipWidth(_.Icon, _.Text)) +
        (widestChips.Length - 1) * LogicalToDeviceUnits(chipSpacing);

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

    int Measure(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        return TextWidth(text, Font, TextFormatFlags.NoPadding);
    }

    int TextWidth(string text, Font font, TextFormatFlags flags)
    {
        var key = (text, font.Style, flags);
        if (widths.TryGetValue(key, out var width))
        {
            return width;
        }

        width = TextRenderer.MeasureText(text, font, Size.Empty, flags).Width;
        widths[key] = width;
        return width;
    }

    static void Draw(Graphics graphics, string text, Font font, int x, Rectangle bounds, int width, Color colour) =>
        TextRenderer.DrawText(graphics, text, font, new Rectangle(x, bounds.Top, width, bounds.Height), colour, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    int RowAt(int y)
    {
        if (page is null)
        {
            return -1;
        }

        var row = y / RowHeight;
        if (row >= 0 && row < page.Rows.Count)
        {
            return row;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        var row = RowAt(args.Y);
        var hit = chips.FirstOrDefault(_ => _.Bounds.Contains(args.Location));
        Cursor = hit.Bounds == Rectangle.Empty ? Cursors.Default : Cursors.Hand;
        var link = IsTextLink(hit) ? hit.Bounds : Rectangle.Empty;
        ShowTip(args.Location);
        if (row != hoverRow ||
            link != hoverLink)
        {
            hoverRow = row;
            hoverLink = link;
            Invalidate();
        }

        base.OnMouseMove(args);
    }

    /// <summary>
    /// Whether what the pointer is over is a link in the text, which underlines. The status square
    /// and the provider icon report a kind like a link but are pictures: the hand is all the
    /// feedback they get, and a line under either would read as part of the drawing.
    /// </summary>
    bool IsTextLink((int Row, ChipKind Chip, bool Overflow, Rectangle Bounds) hit)
    {
        if (hit.Overflow ||
            hit.Chip is not (ChipKind.Build or ChipKind.Branch or ChipKind.Repo))
        {
            return false;
        }

        return hit.Bounds.Height <= Font.Height;
    }

    /// <summary>
    /// Records a hover text, dropping the empty ones so a cell with nothing of its own to say
    /// leaves the row's own text showing rather than blanking it.
    /// </summary>
    void Tip(Rectangle bounds, string text)
    {
        if (text.Length > 0)
        {
            tips.Add((bounds, text));
        }
    }

    void ShowTip(Point at)
    {
        var text = TipAt(at);
        // Still over the same thing, so the wait, or what is already up, carries on. Without this
        // every pixel of movement inside one cell would start the delay again and nothing would
        // ever be shown.
        if (text == tipPending)
        {
            return;
        }

        tipPending = text;
        tipTimer.Stop();
        HideTip();
        if (text.Length == 0)
        {
            return;
        }

        tipAt = at;
        tipTimer.Start();
    }

    void ShowPendingTip()
    {
        tipTimer.Stop();
        if (tipPending.Length == 0)
        {
            return;
        }

        toolTip.Show(tipPending, this, tipAt.X + tipOffset, tipAt.Y + tipOffset, tipDuration);
        tipVisible = true;
    }

    void HideTip()
    {
        if (tipVisible)
        {
            toolTip.Hide(this);
            tipVisible = false;
        }
    }

    /// <summary>
    /// The hover text under the pointer: the last one recorded that covers it, so a cell's own text
    /// wins over the one for the whole row it was drawn on.
    /// </summary>
    string TipAt(Point at)
    {
        for (var index = tips.Count - 1; index >= 0; index--)
        {
            if (tips[index].Bounds.Contains(at))
            {
                return tips[index].Text;
            }
        }

        return "";
    }

    protected override void OnMouseLeave(EventArgs args)
    {
        hoverRow = -1;
        hoverLink = Rectangle.Empty;
        ShowTip(new(-1, -1));
        Invalidate();
        base.OnMouseLeave(args);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        // Whatever the click does, a tooltip left over the row it was aimed at is in the way of it.
        tipTimer.Stop();
        HideTip();
        Focus();
        var row = RowAt(args.Y);
        if (args.Button == MouseButtons.Right)
        {
            rightClickedRow = row;
        }
        else if (args.Button == MouseButtons.Left)
        {
            var chip = chips.FirstOrDefault(_ => _.Bounds.Contains(args.Location));
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
            else if (args.Clicks < 2 ||
                     !IsGroup(row))
            {
                // The second press of a double click on a group is dropped: the first already
                // toggled it, and a second toggle would close what was just opened.
                clickedRow = row;
            }
        }

        base.OnMouseDown(args);
    }

    bool IsGroup(int row) =>
        row >= 0 &&
        page is not null &&
        page.Rows[row].Kind == RowKind.Group;

    protected override void OnMouseDoubleClick(MouseEventArgs args)
    {
        var row = RowAt(args.Y);
        if (args.Button == MouseButtons.Left &&
            row >= 0 &&
            !IsGroup(row) &&
            !chips.Any(_ => _.Bounds.Contains(args.Location)))
        {
            Key = CommandKind.OpenBuild;
        }

        base.OnMouseDoubleClick(args);
    }

    protected override void OnMouseWheel(MouseEventArgs args)
    {
        // The rows are about to move out from under it.
        HideTip();
        scrollDelta -= args.Delta / 40;
        base.OnMouseWheel(args);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is
            Keys.Up or
            Keys.Down or
            Keys.Home or
            Keys.End or
            Keys.PageUp or
            Keys.PageDown or
            Keys.Enter ||
        base.IsInputKey(keyData);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            contextMenu.Dispose();
            tipTimer.Dispose();
            toolTip.Dispose();
            bold.Dispose();
            underline.Dispose();
            boldUnderline.Dispose();
        }

        base.Dispose(disposing);
    }
}
