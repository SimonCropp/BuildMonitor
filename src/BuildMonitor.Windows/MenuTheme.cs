/// <summary>
/// Dark or light menus, drawn by PlatformHeads' <see cref="PaletteMenuRenderer" />. Applied each
/// time a menu opens, so the renderer is made from the palette as it is then, and a menu follows a
/// change of theme as it did when the renderer read the palette itself.
/// </summary>
static class MenuTheme
{
    public static void Apply(ToolStrip strip)
    {
        strip.Renderer = new PaletteMenuRenderer(Colours());
        strip.BackColor = Palette.Surface;
        strip.ForeColor = Palette.Text;
    }

    static MenuPalette Colours() =>
        new(
            Background: Palette.Surface,
            Border: Palette.Border,
            ItemBorder: Palette.Border,
            Selected: Palette.SelectedRow,
            Pressed: Palette.SelectedRow,
            Separator: Palette.Border,
            Text: Palette.Text,
            Dim: Palette.Dim);
}
