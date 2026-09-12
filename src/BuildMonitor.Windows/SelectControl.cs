/// <summary>
/// A drop down list drawn by hand: the value and an arrow, with the options in a themed menu.
/// The native ComboBox neither follows the dark palette nor paints its text into an offscreen
/// capture, and a select needs nothing else a ComboBox offers.
/// </summary>
sealed class SelectControl : Control
{
    readonly ContextMenuStrip menu = new();
    string value = "";
    IReadOnlyList<string> options = [];

    public event EventHandler? ValueChanged;

    public SelectControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        BackColor = Palette.Surface;
        ForeColor = Palette.Text;
        Cursor = Cursors.Hand;
        Size = new(260, 24);
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
        get => options;
        set
        {
            options = value;
            menu.Items.Clear();
            foreach (var option in options)
            {
                menu.Items.Add(new ToolStripMenuItem(option) { ForeColor = Palette.Text });
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Enabled ? Palette.Surface : Palette.Background);
        using (var pen = new Pen(Palette.Border))
        {
            graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        var textBounds = new Rectangle(6, 0, Width - 30, Height);
        TextRenderer.DrawText(graphics, value, Font, textBounds, Enabled ? Palette.Text : Palette.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, "▾", Font, new Rectangle(Width - 22, 0, 18, Height), Palette.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (Enabled &&
            e.Button == MouseButtons.Left)
        {
            menu.Show(this, new Point(0, Height));
        }

        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled &&
            e.KeyCode is Keys.Space or Keys.Enter or Keys.Down)
        {
            menu.Show(this, new Point(0, Height));
            e.Handled = true;
        }

        base.OnKeyDown(e);
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
