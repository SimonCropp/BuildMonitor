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
