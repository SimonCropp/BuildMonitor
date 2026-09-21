/// <summary>
/// The one window. Header, a body that is either the owner drawn rows or the form controls,
/// a footer of real buttons and a status label. Closing hides; only <see cref="AllowClose"/>
/// lets it go, which the exit path sets.
/// </summary>
sealed class MonitorForm : Form
{
    readonly HeaderPanel header;
    readonly RowsCanvas canvas;
    readonly VScrollBar scrollBar;
    readonly FormPanel formPanel;
    readonly FooterPanel footer;
    Screen? last;
    bool closeRequested;
    bool suppressScroll;
    // Where the window settled, waiting for the next Drain to report it.
    WindowPlacement? settled;
    FormWindowState settledState;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public MonitorForm(string title, int width, int height, WindowPlacement? placement = null)
    {
        Text = title;
        Icon = Icons.Window;
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        ClientSize = LogicalToDeviceUnits(new Size(width, height));
        MinimumSize = LogicalToDeviceUnits(new Size(560, 320));
        StartPosition = FormStartPosition.CenterScreen;
        if (placement is not null)
        {
            Place(placement);
        }

        DoubleBuffered = true;
        KeyPreview = true;
        Font = new("Segoe UI", 11f);

        header = new()
        {
            Dock = DockStyle.Top
        };
        footer = new()
        {
            Dock = DockStyle.Bottom
        };
        canvas = new()
        {
            Dock = DockStyle.Fill
        };
        scrollBar = new()
        {
            Dock = DockStyle.Right,
            TabStop = false,
            Visible = false
        };
        scrollBar.Scroll += (_, _) =>
        {
            if (!suppressScroll)
            {
                canvas.ScrollTo = scrollBar.Value;
            }
        };
        formPanel = new()
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        Controls.Add(canvas);
        Controls.Add(formPanel);
        Controls.Add(scrollBar);
        Controls.Add(footer);
        Controls.Add(header);
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Opens where it was left, unless no screen can reach that any more: a monitor unplugged since,
    /// or a laptop taken off its dock, would open it where it could be neither seen nor dragged
    /// back. It opens centred at its first size then, as on a first start.
    /// </summary>
    void Place(WindowPlacement placement)
    {
        var bounds = new Rectangle(placement.X, placement.Y, placement.Width, placement.Height);
        if (!Reachable(bounds))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (placement.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        settledState = WindowState;
    }

    /// <summary>
    /// Whether enough of the title bar is on a screen to drag the window by. The title bar rather
    /// than any of the window: a window whose top is off every screen cannot be moved back.
    /// </summary>
    static bool Reachable(Rectangle bounds)
    {
        var caption = new Rectangle(bounds.X, bounds.Y, bounds.Width, SystemInformation.CaptionHeight);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            if (Rectangle.Intersect(screen.WorkingArea, caption) is { Width: >= 100, Height: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The end of a drag, by the title bar or an edge. Not every frame of it: each report is a
    /// save.
    /// </summary>
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        Settle();
    }

    /// <summary>
    /// A maximize or a restore, which the caption buttons and a double click on the title bar do
    /// without the drag that ends in <see cref="OnResizeEnd"/>.
    /// </summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (WindowState != settledState)
        {
            Settle();
        }
    }

    /// <summary>
    /// A hide, which is how the window is left every time: it catches what the two above do not,
    /// such as a snap to half the screen from the keyboard.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            Settle();
        }
    }

    /// <summary>
    /// Notes where the window is for the next <see cref="Drain"/>: its bounds when not maximized,
    /// which RestoreBounds holds while it is, so a restore after the next start goes back to the
    /// size it had. A minimized window leaves the last placement standing, since opening minimized
    /// would look like not opening at all.
    /// </summary>
    void Settle()
    {
        settledState = WindowState;
        if (WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var maximized = WindowState == FormWindowState.Maximized;
        var bounds = Bounds;
        if (maximized)
        {
            bounds = RestoreBounds;
        }

        settled = new(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose &&
            e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            closeRequested = true;
            return;
        }

        base.OnFormClosing(e);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (formPanel.Visible &&
            ActiveControl is TextBox or SelectControl &&
            e.KeyCode != Keys.Escape)
        {
            return;
        }

        if (!formPanel.Visible &&
            e is { Control: true, KeyCode: Keys.F })
        {
            header.FocusSearch();
            Handled(e);
            return;
        }

        // The filter box keeps what editing needs: letters, Home, End and the clipboard. The keys
        // that move through the rows still reach them, so a filter can be typed and its match opened
        // without leaving the box, and Escape empties a box with text in it before it hides.
        if (header.SearchFocused)
        {
            if (e.KeyCode == Keys.Escape &&
                header.ClearSearch())
            {
                Handled(e);
                return;
            }

            if (e.KeyCode is not (Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Enter or Keys.Escape or Keys.F5) &&
                e is not { Control: true, KeyCode: Keys.Q })
            {
                return;
            }
        }

        var command = Command(e.KeyCode, e.Control, e.Shift, formPanel.Visible);
        if (command == CommandKind.None)
        {
            return;
        }

        canvas.Key = command;
        Handled(e);
    }

    /// <summary>
    /// The command a key asks for, on a form page or the builds page, or None. Apart from the
    /// handler, which also has the focus to weigh, so the keymap can be tested without a window.
    /// </summary>
    public static CommandKind Command(Keys key, bool control, bool shift, bool form) =>
        key switch
        {
            Keys.Up => CommandKind.PreviousRow,
            Keys.Down => CommandKind.NextRow,
            Keys.PageUp => CommandKind.PageUp,
            Keys.PageDown => CommandKind.PageDown,
            Keys.Home => CommandKind.ScrollHome,
            Keys.End => CommandKind.ScrollEnd,
            Keys.Enter when !form => CommandKind.OpenBuild,
            // With Control, as every key that changes a service's builds is: R alone, typed into
            // the rows by someone who took the filter box to have the keyboard, reran the build.
            Keys.R when control && !form => CommandKind.Retry,
            Keys.OemPeriod when control && !form => CommandKind.Cancel,
            Keys.L when control && !form => CommandKind.CopyLog,
            Keys.T when control && !form => CommandKind.Triage,
            Keys.F10 when shift && !form => CommandKind.OpenMenu,
            Keys.Apps when !form => CommandKind.OpenMenu,
            Keys.C when control && !form => CommandKind.CopyBuildUrl,
            Keys.F5 => CommandKind.Refresh,
            Keys.Escape when form => CommandKind.CancelForm,
            Keys.Escape => CommandKind.Hide,
            Keys.Q when control => CommandKind.Quit,
            _ => CommandKind.None
        };

    static void Handled(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    public void Apply(Screen screen)
    {
        if (ReferenceEquals(last, screen) ||
            (last is not null && last == screen))
        {
            return;
        }

        last = screen;
        if (Palette.Use(screen.Theme))
        {
            Retheme();
        }

        header.Apply(screen.Builds?.Header ?? screen.Form?.Title ?? "", screen.Builds?.Search);
        if (screen.Builds is { } builds)
        {
            formPanel.Visible = false;
            canvas.Visible = true;
            canvas.Apply(builds, screen.Menu);
            var visible = Math.Max(1, canvas.VisibleRows);
            scrollBar.Visible = builds.TotalRows > visible;
            if (scrollBar.Visible)
            {
                suppressScroll = true;
                scrollBar.Minimum = 0;
                scrollBar.Maximum = Math.Max(0, builds.TotalRows - 1);
                scrollBar.LargeChange = visible;
                scrollBar.SmallChange = 1;
                scrollBar.Value = Math.Clamp(builds.ScrollTop, 0, Math.Max(0, builds.TotalRows - visible));
                suppressScroll = false;
            }
        }
        else if (screen.Form is { } form)
        {
            canvas.Visible = false;
            scrollBar.Visible = false;
            formPanel.Visible = true;
            formPanel.Apply(form);
        }

        footer.Apply(screen.Buttons, screen.Status);
    }

    /// <summary>
    /// Pushes the palette into every control that copied a colour when it was made. Without this
    /// a theme change on save would leave the header, footer and form controls in the old theme
    /// until a restart.
    /// </summary>
    void Retheme()
    {
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        header.Retheme();
        canvas.Retheme();
        footer.Retheme();
        formPanel.Retheme();
        Invalidate(true);
    }

    public MonitorInput Drain()
    {
        var drained = canvas.Drain();
        var input = drained with
        {
            Key = footer.DrainStatusClicked() ? CommandKind.CopyStatus : drained.Key,
            ClickedButton = footer.DrainClickedButton(),
            FieldChanges = formPanel.DrainChanges(),
            ClickedField = formPanel.DrainClickedField(),
            Search = header.DrainSearch(),
            CloseRequested = closeRequested,
            Placement = settled,
            Columns = 120,
            Rows = Math.Max(1, canvas.VisibleRows) + ScreenBuilder.Chrome
        };
        closeRequested = false;
        settled = null;
        return input;
    }
}
