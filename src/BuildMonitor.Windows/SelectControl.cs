/// <summary>
/// A drop down list drawn by hand: the value and an arrow, with the options in a themed menu.
/// The native ComboBox neither follows the dark palette nor paints its text into an offscreen
/// capture, and a select needs nothing else a ComboBox offers.
/// </summary>
sealed class SelectControl : Control
{
    ContextMenuStrip menu = new();
    string value = "";

    public event EventHandler? ValueChanged;

    public SelectControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable,
            true);
        BackColor = Palette.Surface;
        ForeColor = Palette.Text;
        Cursor = Cursors.Hand;
        Size = LogicalToDeviceUnits(new Size(260, 28));
        MenuTheme.Apply(menu);
        menu.ItemClicked += (_, arguments) =>
        {
            if (arguments.ClickedItem?.Text is { } chosen &&
                chosen != value)
            {
                value = chosen;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => value;
        set
        {
            if (this.value == value)
            {
                return;
            }

            this.value = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Options
    {
        get;
        set
        {
            field = value;
            menu.Items.Clear();
            foreach (var option in field)
            {
                menu.Items.Add(
                    new ToolStripMenuItem(option)
                    {
                        ForeColor = Palette.Text
                    });
            }
        }
    } = [];

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.Clear(Enabled ? Palette.Surface : Palette.Background);
        using (var pen = new Pen(Palette.Border))
        {
            graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        var textBounds = new Rectangle(LogicalToDeviceUnits(6), 0, Width - LogicalToDeviceUnits(30), Height);
        TextRenderer.DrawText(graphics, value, Font, textBounds, Enabled ? Palette.Text : Palette.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, "▾", Font, new Rectangle(Width - LogicalToDeviceUnits(22), 0, LogicalToDeviceUnits(18), Height), Palette.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        Focus();
        if (Enabled &&
            args.Button == MouseButtons.Left)
        {
            ShowMenu();
        }

        base.OnMouseDown(args);
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (Enabled &&
            args.KeyCode is Keys.Space or Keys.Enter or Keys.Down)
        {
            ShowMenu();
            args.Handled = true;
        }

        base.OnKeyDown(args);
    }

    void ShowMenu()
    {
        MenuTheme.Apply(menu);
        menu.Show(this, new(0, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            menu.Dispose();
        }

        base.Dispose(disposing);
    }
}
