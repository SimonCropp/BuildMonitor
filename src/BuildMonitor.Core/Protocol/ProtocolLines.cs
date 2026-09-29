/// <summary>
/// The "name: base64\n" lines a <see cref="Message"/> and a <see cref="Response"/> are made of,
/// written and read as UTF-8 rather than as strings. A body is a listing or a log of a few
/// megabytes, and going through strings copied it five times over on each side of the socket.
/// </summary>
static class ProtocolLines
{
    /// <summary>
    /// The exact bytes <see cref="WriteField"/> takes, so a message is written into one array
    /// sized up front rather than one that grows, and copies, as it fills.
    /// </summary>
    public static int FieldLength(ReadOnlySpan<byte> name, string value) =>
        name.Length + 2 + Base64.GetMaxEncodedToUtf8Length(Encoding.UTF8.GetByteCount(value)) + 1;

    /// <summary>
    /// The value's UTF-8 lands where its base64 is to go and is encoded in place, so no array of
    /// the raw bytes is made beside the one being written.
    /// </summary>
    public static int WriteField(Span<byte> destination, ReadOnlySpan<byte> name, string value)
    {
        var position = Write(destination, name);
        position += Write(destination[position..], ": "u8);
        var target = destination[position..];
        var count = Encoding.UTF8.GetBytes(value, target);
        Base64.EncodeToUtf8InPlace(target, count, out var written);
        position += written;
        position += Write(destination[position..], "\n"u8);
        return position;
    }

    public static int Write(Span<byte> destination, ReadOnlySpan<byte> text)
    {
        text.CopyTo(destination);
        return text.Length;
    }

    /// <summary>
    /// The next line that has a name, consumed from <paramref name="rest"/>. A line with no colon
    /// is skipped, as it was when the text was split, so a newer sender's lines cannot break an
    /// older reader.
    /// </summary>
    public static bool TryReadField(ref ReadOnlySpan<byte> rest, out ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value)
    {
        while (rest.Length > 0)
        {
            ReadOnlySpan<byte> line;
            var newline = rest.IndexOf((byte) '\n');
            if (newline < 0)
            {
                line = rest;
                rest = default;
            }
            else
            {
                line = rest[..newline];
                rest = rest[(newline + 1)..];
            }

            var separator = line.IndexOf((byte) ':');
            if (separator < 0)
            {
                continue;
            }

            name = Trim(line[..separator]);
            value = Trim(line[(separator + 1)..]);
            return true;
        }

        name = default;
        value = default;
        return false;
    }

    static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> text) =>
        text[Ascii.Trim(text)];

    /// <summary>
    /// Empty rather than an exception for a value that is not base64, so a garbled key reads as
    /// no build rather than taking the tray's listener down. The decoded bytes are pooled, since
    /// only the string made from them outlives the call.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> value)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Base64.GetMaxDecodedFromUtf8Length(value.Length));
        try
        {
            if (Base64.DecodeFromUtf8(value, buffer, out _, out var written) != OperationStatus.Done)
            {
                return "";
            }

            return Encoding.UTF8.GetString(buffer, 0, written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
