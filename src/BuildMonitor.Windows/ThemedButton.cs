/// <summary>
/// A flat button that draws its own label while disabled. WinForms derives a disabled flat
/// button's text colour from its <see cref="Control.BackColor"/> and ignores
/// <see cref="Control.ForeColor"/>, which on the dark theme's chip came out as near black on dark
/// grey: a footer button waiting on a field read as an empty box.
/// </summary>
sealed class ThemedButton : FormsButton
{
    public ThemedButton()
    {
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
        Retheme();
    }

    public void Retheme()
    {
        ForeColor = Palette.Text;
        BackColor = Palette.Chip;
        FlatAppearance.BorderColor = Palette.Border;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        if (Enabled)
        {
            return;
        }

        var graphics = pevent.Graphics;
        var bounds = ClientRectangle;
        using var fill = new SolidBrush(BackColor);
        graphics.FillRectangle(fill, bounds);
        using var border = new Pen(FlatAppearance.BorderColor);
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        if (!UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        TextRenderer.DrawText(graphics, Text, Font, bounds, Palette.Dim, flags);
    }
}
