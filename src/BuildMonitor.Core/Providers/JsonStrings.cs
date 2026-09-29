/// <summary>
/// One string member of a JSON object a pipeline wrote as text: build variables, release notes. The
/// object is read with a <see cref="Utf8JsonReader"/> as far as the name, rather than parsed into a
/// document to look one name up, and anything that is not such an object names nothing rather than
/// throwing: it is whatever the pipeline wrote, and one that did not parse would otherwise fail the
/// fetch it arrived in.
/// </summary>
static class JsonStrings
{
    /// <summary>
    /// The first top level member called <paramref name="name"/> whose value is a string, or null
    /// where there is none, the text is not a JSON object, or it breaks off before one is found.
    /// </summary>
    public static string? Find(ReadOnlySpan<byte> json, string name, StringComparison comparison)
    {
        var reader = new Utf8JsonReader(json);
        try
        {
            if (!reader.Read() ||
                reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() &&
                   reader.TokenType == JsonTokenType.PropertyName)
            {
                var named = Named(ref reader, name, comparison);
                reader.Read();
                if (named &&
                    reader.TokenType == JsonTokenType.String)
                {
                    return reader.GetString();
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// The same over text, transcoded to UTF-8 in a pooled buffer.
    /// </summary>
    public static string? Find(string json, string name, StringComparison comparison)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(json.Length));
        try
        {
            var length = Encoding.UTF8.GetBytes(json, buffer);
            return Find(buffer.AsSpan(0, length), name, comparison);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Compared as it sits in the UTF-8 where that is exact, and otherwise decoded onto the stack,
    /// so no string is made of a name that is not the one wanted.
    /// </summary>
    static bool Named(ref Utf8JsonReader reader, string name, StringComparison comparison)
    {
        if (comparison == StringComparison.Ordinal)
        {
            return reader.ValueTextEquals(name);
        }

        var length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (length > 256)
        {
            return string.Equals(reader.GetString(), name, comparison);
        }

        // A name decodes to no more UTF-16 characters than it has UTF-8 bytes.
        Span<char> chars = stackalloc char[(int) length];
        var written = reader.CopyString(chars);
        return chars[..written].Equals(name, comparison);
    }
}
