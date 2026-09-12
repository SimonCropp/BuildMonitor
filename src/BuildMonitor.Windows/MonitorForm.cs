/// <summary>
/// The one window. Header, a body that is either the owner drawn rows or the form controls,
/// a footer of real buttons and a status label. Closing hides; only <see cref="AllowClose"/>
/// lets it go, which the exit path sets.
/// </summary>
sealed class MonitorForm : Form
{
    readonly FormsLabel header;
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
        ClientSize = new(width, height);
        MinimumSize = new(560, 320);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        Font = new("Segoe UI", 9.5f);

        header = new()
        {
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new(10, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Palette.Dim,
            BackColor = Palette.Surface
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
            ActiveControl is TextBox or ComboBox &&
            e.KeyCode != Keys.Escape)
        {
            return;
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
        header.Text = screen.Builds?.Header ?? screen.Form?.Title ?? "";
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

    public MonitorInput Drain()
    {
        var input = canvas.Drain() with
        {
            ClickedButton = footer.DrainClickedButton(),
            FieldChanges = formPanel.DrainChanges(),
            ClickedField = formPanel.DrainClickedField(),
            CloseRequested = closeRequested,
            Columns = 120,
            Rows = Math.Max(1, canvas.VisibleRows) + ScreenBuilder.Chrome
        };
        closeRequested = false;
        return input;
    }
}
