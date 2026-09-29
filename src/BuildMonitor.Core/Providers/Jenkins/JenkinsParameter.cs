/// <summary>
/// One parameter a build was started with.
/// </summary>
class JenkinsParameter
{
    public string? Name { get; set; }

    /// <summary>
    /// The value where it is text, and nothing where it is not: only a name or an id is of use,
    /// and neither is a number or a flag. A parameter is whatever type the job declared it, so a
    /// boolean or a number arrives as one, and read as a string it would fail the whole job's
    /// fetch over a parameter nothing here wanted.
    /// </summary>
    [JsonConverter(typeof(TextOrNothingConverter))]
    public string? Value { get; set; }
}
