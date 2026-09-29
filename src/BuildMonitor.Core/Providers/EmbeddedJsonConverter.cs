/// <summary>
/// A JSON document a service wrote into a string, kept as the UTF-8 it unescapes to so that
/// <see cref="JsonStrings"/> can read it where it is asked. Read as a string, it was parsed into a
/// document afresh for each name looked up, three times a build, after being transcoded back to
/// the UTF-8 it arrived as. A value that is not a string is nothing rather than a failed fetch.
/// </summary>
sealed class EmbeddedJsonConverter : JsonConverter<byte[]?>
{
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        // Unescaping only shortens, and a document written into a string is mostly escaped quotes,
        // so the escaped length is borrowed and only what it unescapes to is kept.
        var length = reader.HasValueSequence ? checked((int) reader.ValueSequence.Length) : reader.ValueSpan.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var written = reader.CopyString(buffer);
            return buffer.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override void Write(Utf8JsonWriter writer, byte[]? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
