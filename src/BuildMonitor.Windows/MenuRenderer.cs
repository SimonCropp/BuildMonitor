sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColours())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
    {
        args.TextColor = args.Item.Enabled ? Palette.Text : Palette.Dim;
        base.OnRenderItemText(args);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs args)
    {
        args.ArrowColor = Palette.Text;
        base.OnRenderArrow(args);
    }
}
