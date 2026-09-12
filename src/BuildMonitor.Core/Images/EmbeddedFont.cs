/// <summary>
/// The font the native heads draw with, so text measures the same on every machine and the
/// pixel snapshots hold. Absent from a build that has no font embedded, in which case the
/// library falls back to its own.
/// </summary>
static class EmbeddedFont
{
    public static byte[] Bytes()
    {
        using var stream = typeof(EmbeddedFont).Assembly.GetManifestResourceStream("BuildMonitor.Assets.JetBrainsMono-Regular.ttf");
        if (stream is null)
        {
            return [];
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
