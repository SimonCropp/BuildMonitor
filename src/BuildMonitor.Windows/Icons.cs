/// <summary>
/// The embedded images as GDI+ objects, decoded once. Every accessor tolerates a missing
/// resource, so a build that never ran IconBuilder shows text without pictures.
/// </summary>
static class Icons
{
    static readonly ConcurrentDictionary<string, Icon?> icons = new();
    static readonly ConcurrentDictionary<string, Bitmap?> bitmaps = new();

    public static Icon? Tray(TrayIconKind kind) =>
        icons.GetOrAdd($"tray-{Images.TrayName(kind)}", _ =>
        {
            using var stream = Images.Open($"{_}.ico");
            return stream is null ? null : new Icon(stream, SystemInformation.SmallIconSize);
        });

    public static Icon? Window =>
        icons.GetOrAdd("window", _ =>
        {
            using var stream = Images.Open("tray-idle.ico");
            return stream is null ? null : new Icon(stream);
        });

    public static Bitmap? Glyph(string name, int size = 16) =>
        bitmaps.GetOrAdd($"{name}-{size}", _ =>
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
