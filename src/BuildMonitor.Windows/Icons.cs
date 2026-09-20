/// <summary>
/// The embedded images as GDI+ objects, decoded once. Every accessor tolerates a missing
/// resource, so a build that never ran IconBuilder shows text without pictures.
/// </summary>
static class Icons
{
    static ConcurrentDictionary<string, Icon?> icons = new();
    static ConcurrentDictionary<string, Bitmap?> bitmaps = new();

    public static Icon? Tray(TrayIconKind kind) =>
        icons.GetOrAdd(
            $"tray-{Images.TrayName(kind)}",
            _ =>
            {
                using var stream = Images.Open($"{_}.ico");
                if (stream is null)
                {
                    return null;
                }

                return new(stream, SystemInformation.SmallIconSize);
            });

    public static Icon? Window =>
        icons.GetOrAdd(
            "window",
            _ =>
            {
                using var stream = Images.Open("tray-idle.ico");
                if (stream is null)
                {
                    return null;
                }

                return new(stream);
            });

    /// <summary>
    /// Draws a glyph into <paramref name="bounds"/>, and says whether it drew one: a build that
    /// never ran IconBuilder has no resource to draw, and a caller that hit tests the picture has
    /// nothing to hit test.
    /// <para>
    /// Every caller goes through this rather than drawing the cached <see cref="Bitmap"/> itself,
    /// because one instance is shared by every caller and GDI+ refuses to draw an image two
    /// threads are in at once: "Object is currently in use elsewhere". The tray only ever paints on
    /// its own thread, but the snapshot tests paint several windows at once.
    /// </para>
    /// </summary>
    public static bool Draw(Graphics graphics, string name, Rectangle bounds, int size = 16)
    {
        if (Glyph(name, size) is not { } glyph)
        {
            return false;
        }

        lock (glyph)
        {
            graphics.DrawImage(glyph, bounds);
        }

        return true;
    }

    public static Bitmap? Glyph(string name, int size = 16) =>
        bitmaps.GetOrAdd(
            $"{name}-{size}",
            _ =>
            {
                var bytes = Images.Glyph(name, size);
                if (bytes is null)
                {
                    return null;
                }

                using var stream = new MemoryStream(bytes);
                return new(stream);
            });
}
