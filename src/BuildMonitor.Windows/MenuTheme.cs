/// <summary>
/// Dark menus. Assigned per strip rather than through ToolStripManager.Renderer, which is
/// process wide state a test host would share.
/// </summary>
sealed class MenuColours : ProfessionalColorTable
{
    public override Color MenuItemSelected => Palette.SelectedRow;
    public override Color MenuItemSelectedGradientBegin => Palette.SelectedRow;
    public override Color MenuItemSelectedGradientEnd => Palette.SelectedRow;
    public override Color MenuItemPressedGradientBegin => Palette.SelectedRow;
    public override Color MenuItemPressedGradientEnd => Palette.SelectedRow;
    public override Color MenuItemBorder => Palette.Border;
    public override Color MenuBorder => Palette.Border;
    public override Color ToolStripDropDownBackground => Palette.Surface;
    public override Color ImageMarginGradientBegin => Palette.Surface;
    public override Color ImageMarginGradientMiddle => Palette.Surface;
    public override Color ImageMarginGradientEnd => Palette.Surface;
    public override Color SeparatorDark => Palette.Border;
    public override Color SeparatorLight => Palette.Border;
}

sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColours())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Palette.Text;
        base.OnRenderArrow(e);
    }
}

static class MenuTheme
{
    public static void Apply(ToolStrip strip)
    {
        strip.Renderer = new MenuRenderer();
        strip.BackColor = Palette.Surface;
        strip.ForeColor = Palette.Text;
    }
}
