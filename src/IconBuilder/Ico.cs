/// <summary>
/// An ICO container of PNG frames, which Windows has read since Vista.
/// </summary>
static class Ico
{
    public static byte[] Pack(IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort) 0);
        writer.Write((ushort) 1);
        writer.Write((ushort) frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            // 256 is written as 0 in the single byte the directory has for it.
            writer.Write((byte) (size >= 256 ? 0 : size));
            writer.Write((byte) (size >= 256 ? 0 : size));
            writer.Write((byte) 0);
            writer.Write((byte) 0);
            writer.Write((ushort) 1);
            writer.Write((ushort) 32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
