/// <summary>
/// Reads every string, and every dictionary key, through <see cref="StringPool.Shared"/>. Named in
/// each provider's context, so the parsed values <see cref="ETagCache"/> keeps between polls, and
/// the builds made from them, share one copy of each value the services repeat. A property with a
/// converter of its own, <see cref="TextOrNothingConverter"/> say, still reads through that one.
/// </summary>
sealed class PooledStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringPool.Shared.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    /// <summary>
    /// Without it a dictionary keyed by string, as Octopus's permissions are, would fail to parse:
    /// a converter of a program's own reads no keys unless it says how.
    /// </summary>
    public override string? ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringPool.Shared.Read(ref reader);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value!);
}
