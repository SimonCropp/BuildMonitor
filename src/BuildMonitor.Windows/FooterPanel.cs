/// <summary>
/// The footer: real buttons, one per <see cref="Button"/>, and the status line, which a click
/// copies, as a label's text can not be selected and an error in it can run past its end.
/// </summary>
sealed class FooterPanel : Panel
{
    FlowLayoutPanel buttons;
    LiveLabel status;
    List<FormsButton> pool = [];
    string signature = "";
    int clicked = -1;
    bool statusClicked;

    public FooterPanel()
    {
        Height = LogicalToDeviceUnits(48);
        BackColor = Palette.Surface;
        buttons = new()
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            WrapContents = false,
            Padding = DpiScale.Spacing(this, 6, 6, 0, 0),
            BackColor = Palette.Surface
        };
        status = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = DpiScale.Spacing(this, 0, 0, 12, 0),
            ForeColor = Palette.Dim,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };
        status.Click += (_, _) => statusClicked = true;
        Controls.Add(status);
        Controls.Add(buttons);
    }

    public void Apply(IReadOnlyList<Button> model, string statusText)
    {
        status.Text = statusText;
        var next = string.Join('|', model.Select(_ => _.Label));
        if (next != signature)
        {
            signature = next;
            buttons.SuspendLayout();
            buttons.Controls.Clear();
            while (pool.Count < model.Count)
            {
                var index = pool.Count;
                var button = new FormsButton
                {
                    AutoSize = true,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Palette.Text,
                    BackColor = Palette.Chip,
                    Margin = DpiScale.Spacing(this, 4, 2, 4, 2),
                    MinimumSize = LogicalToDeviceUnits(new Size(80, 30))
                };
                button.FlatAppearance.BorderColor = Palette.Border;
                button.Click += (_, _) => clicked = index;
                pool.Add(button);
            }

            for (var index = 0; index < model.Count; index++)
            {
                pool[index].Text = model[index].Label;
                buttons.Controls.Add(pool[index]);
            }

            buttons.ResumeLayout();
        }

        for (var index = 0; index < model.Count; index++)
        {
            pool[index].Enabled = model[index].Enabled;
        }
    }

    public void Retheme()
    {
        BackColor = Palette.Surface;
        buttons.BackColor = Palette.Surface;
        status.ForeColor = Palette.Dim;
        foreach (var button in pool)
        {
            button.ForeColor = Palette.Text;
            button.BackColor = Palette.Chip;
            button.FlatAppearance.BorderColor = Palette.Border;
        }
    }

    public int DrainClickedButton()
    {
        var value = clicked;
        clicked = -1;
        return value;
    }

    public bool DrainStatusClicked()
    {
        var value = statusClicked;
        statusClicked = false;
        return value;
    }
}
