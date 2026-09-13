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
