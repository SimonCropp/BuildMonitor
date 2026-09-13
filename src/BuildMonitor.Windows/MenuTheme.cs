static class MenuTheme
{
    public static void Apply(ToolStrip strip)
    {
        strip.Renderer = new MenuRenderer();
        strip.BackColor = Palette.Surface;
        strip.ForeColor = Palette.Text;
    }
}
