/// <summary>
/// The fonts the native heads draw with, so text measures the same on every machine and the
/// pixel snapshots hold. Either is absent from a build that has none embedded, in which case the
/// library falls back to its own.
/// </summary>
static class EmbeddedFont
{
    public static byte[] Bytes() =>
        Read("JetBrainsMono-Regular.ttf");

    /// <summary>
    /// Noto Emoji cut down to the one mark a row can carry, <see cref="Bots.Mark"/>, to be merged
    /// over the text font. JetBrains Mono has no emoji at all, so without this a Dependabot branch
    /// and a bot author drew a blank box on the heads that rasterise their own text; the ones that
    /// draw through the platform, WinForms and AppKit, fall back to a system font by themselves.
    /// <para>
    /// A subset rather than the whole family, which is nearly a megabyte for glyphs nothing here
    /// draws. Regenerate it after adding a mark, with the codepoints of every one:
    /// <code>python -m fontTools.subset NotoEmoji-Regular.ttf --unicodes=U+1F916 --output-file=NotoEmoji-Robot.ttf</code>
    /// </para>
    /// </summary>
    public static byte[] Emoji() =>
        Read("NotoEmoji-Robot.ttf");

    static byte[] Read(string name)
    {
        using var stream = typeof(EmbeddedFont).Assembly.GetManifestResourceStream($"BuildMonitor.Assets.{name}");
        if (stream is null)
        {
            return [];
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
