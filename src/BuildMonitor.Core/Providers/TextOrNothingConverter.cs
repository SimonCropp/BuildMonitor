/// <summary>
/// A value read as text where it is a string and as nothing where it is anything else, for a field
/// whose type is the service's to choose. Read as a plain string, a number or a flag there failed
/// the whole fetch over a value nothing wanted; held as a <see cref="JsonElement"/>, every value
/// cost a document of its own, copied out of the response, most of them never read.
/// </summary>
sealed class TextOrNothingConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
