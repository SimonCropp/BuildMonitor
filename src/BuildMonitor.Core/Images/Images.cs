/// <summary>
/// The embedded images by name. Null rather than an exception for one that is missing, so a
/// head running from a checkout that never ran IconBuilder still starts, with no icon.
/// </summary>
static class Images
{
    static Assembly assembly = typeof(Images).Assembly;

    public static byte[]? Bytes(string file)
    {
        using var stream = assembly.GetManifestResourceStream($"BuildMonitor.Images.{file}");
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static Stream? Open(string file) =>
        assembly.GetManifestResourceStream($"BuildMonitor.Images.{file}");

    public static string TrayName(TrayIconKind kind) =>
        kind.ToString().ToLowerInvariant();

    public static byte[]? TrayPng(TrayIconKind kind, int size) =>
        Bytes($"tray-{TrayName(kind)}-{size}.png");

    public static byte[]? TrayIco(TrayIconKind kind) =>
        Bytes($"tray-{TrayName(kind)}.ico");

    public static byte[]? TrayArgb(TrayIconKind kind, int size) =>
        Bytes($"tray-{TrayName(kind)}-{size}.argb");

    public static byte[]? Glyph(string name, int size) =>
        Bytes($"glyph-{name}-{size}.png");

    public static IEnumerable<string> All() =>
        assembly.GetManifestResourceNames()
            .Where(_ => _.StartsWith("BuildMonitor.Images.", StringComparison.Ordinal))
            .Select(_ => _["BuildMonitor.Images.".Length..])
            .Order();
}
