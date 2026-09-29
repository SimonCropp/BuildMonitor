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
    /// name, each as its text. Read through <see cref="Property"/>; see
    /// <see cref="AzureDevOpsPropertiesConverter"/> for why no shape can fail it.
    /// </summary>
    [JsonConverter(typeof(AzureDevOpsPropertiesConverter))]
    public Dictionary<string, string?>? Properties { get; set; }

    /// <summary>
    /// One property as text: the value itself, or the <c>$value</c> of the wrapper Azure DevOps
    /// writes a typed property as. Null where the collection is not an object, has no such
    /// property, or holds it as something other than text. The name is matched without case,
    /// because it is whatever the pipeline that wrote the property called it.
    /// </summary>
    public string? Property(string name) =>
        Properties?.GetValueOrDefault(name);

    /// <summary>
    /// The variables the build was queued with, as a JSON object written into a string, which for a
    /// pull request build say which branch it came from and which it targets. Kept as the UTF-8 the
    /// string unescapes to, and read through <see cref="Parameter"/>.
    /// </summary>
    [JsonConverter(typeof(EmbeddedJsonConverter))]
    public byte[]? Parameters { get; set; }

    /// <summary>
    /// One of <see cref="Parameters"/> as text, or null where the string is absent, is not an object
    /// of strings, or has no such name. Read here rather than by the serializer: it is text the
    /// pipeline wrote, and one that did not parse would otherwise fail the project's whole fetch.
    /// </summary>
    public string? Parameter(string name)
    {
        if (Parameters is null)
        {
            return null;
        }

        return JsonStrings.Find(Parameters, name, StringComparison.Ordinal);
    }

    [JsonPropertyName("_links")]
    public AzureDevOpsLinks? Links { get; set; }
}