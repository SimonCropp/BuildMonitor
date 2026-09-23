class AzureDevOpsBuild
{
    public long Id { get; set; }
    public string? BuildNumber { get; set; }
    public string? Status { get; set; }
    public string? Result { get; set; }
    public DateTimeOffset? QueueTime { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? FinishTime { get; set; }
    public string? SourceBranch { get; set; }
    public string? SourceVersion { get; set; }
    public string? Reason { get; set; }
    public AzureDevOpsIdentity? RequestedFor { get; set; }
    public AzureDevOpsDefinition? Definition { get; set; }
    public AzureDevOpsRepository? Repository { get; set; }
    public AzureDevOpsTriggerInfo? TriggerInfo { get; set; }

    /// <summary>
    /// The build's property collection, which carries only the properties a request asked for by
    /// name. Held as the JSON it arrived as, rather than a dictionary: what a property bag looks
    /// like is the service's to choose, and one that arrived as something other than an object of
    /// strings failed the whole project's fetch, taking every row of it off the screen over a
    /// name. Read through <see cref="Property"/>, which no shape can fail.
    /// </summary>
    public JsonElement Properties { get; set; }

    /// <summary>
    /// One property as text: the value itself, or the <c>$value</c> of the wrapper Azure DevOps
    /// writes a typed property as. Null where the collection is not an object, has no such
    /// property, or holds it as something other than text. The name is matched without case,
    /// because it is whatever the pipeline that wrote the property called it.
    /// </summary>
    public string? Property(string name)
    {
        if (Properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in Properties.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }

            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("$value", out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// The variables the build was queued with, as a JSON object written into a string, which for a
    /// pull request build say which branch it came from and which it targets. Read through
    /// <see cref="Parameter"/>.
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// One of <see cref="Parameters"/> as text, or null where the string is absent, is not an object
    /// of strings, or has no such name. Parsed here rather than by the serializer: it is text the
    /// pipeline wrote, and one that did not parse would otherwise fail the project's whole fetch.
    /// </summary>
    public string? Parameter(string name)
    {
        if (Parameters is not { Length: > 0 } text)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    [JsonPropertyName("_links")]
    public AzureDevOpsLinks? Links { get; set; }
}