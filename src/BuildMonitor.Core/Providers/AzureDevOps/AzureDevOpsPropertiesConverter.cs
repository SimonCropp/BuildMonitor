/// <summary>
/// A build's property collection as each property's text: the value itself, or the <c>$value</c>
/// of the wrapper Azure DevOps writes a typed property as, and null for any other shape. What a
/// property bag looks like is the service's to choose, and one that arrived as something other than
/// an object of strings failed the whole project's fetch, taking every row of it off the screen
/// over a name. Held as a <see cref="JsonElement"/> instead, every build cost a document of its
/// own, copied out of the response.
/// <para>
/// Names are matched without case, because they are whatever the pipeline that wrote the property
/// called it, and the first of a name decides, as a search of the object in order did.
/// </para>
/// </summary>
sealed class AzureDevOpsPropertiesConverter : JsonConverter<Dictionary<string, string?>?>
{
    public override Dictionary<string, string?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() &&
               reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString()!;
            reader.Read();
            properties.TryAdd(name, Text(ref reader));
        }

        return properties;
    }

    static string? Text(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        string? text = null;
        while (reader.Read() &&
               reader.TokenType == JsonTokenType.PropertyName)
        {
            var isValue = reader.ValueTextEquals("$value"u8);
            reader.Read();
            if (isValue &&
                reader.TokenType == JsonTokenType.String)
            {
                text ??= reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return text;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string?>? value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, text) in value!)
        {
            writer.WriteString(name, text);
        }

        writer.WriteEndObject();
    }
}
