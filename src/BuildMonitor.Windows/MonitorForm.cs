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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public MonitorForm(string title, int width, int height)
    {
        Text = title;
        Icon = Icons.Window;
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        ClientSize = LogicalToDeviceUnits(new Size(width, height));
        MinimumSize = LogicalToDeviceUnits(new Size(560, 320));
        StartPosition = FormStartPosition.CenterScreen;
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

        var command = e.KeyCode switch
        {
            Keys.Up => CommandKind.PreviousRow,
            Keys.Down => CommandKind.NextRow,
            Keys.PageUp => CommandKind.PageUp,
            Keys.PageDown => CommandKind.PageDown,
            Keys.Home => CommandKind.ScrollHome,
            Keys.End => CommandKind.ScrollEnd,
            Keys.Enter when !formPanel.Visible => CommandKind.OpenBuild,
            Keys.R when !formPanel.Visible => CommandKind.Retry,
            Keys.C when e.Control && !formPanel.Visible => CommandKind.CopyBuildUrl,
            Keys.F5 => CommandKind.Refresh,
            Keys.Escape when formPanel.Visible => CommandKind.CancelForm,
            Keys.Escape => CommandKind.Hide,
            Keys.Q when e.Control => CommandKind.Quit,
            _ => CommandKind.None
        };
        if (command == CommandKind.None)
        {
            return;
        }

        canvas.Key = command;
        Handled(e);
    }

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
            Columns = 120,
            Rows = Math.Max(1, canvas.VisibleRows) + ScreenBuilder.Chrome
        };
        closeRequested = false;
        return input;
    }
}
