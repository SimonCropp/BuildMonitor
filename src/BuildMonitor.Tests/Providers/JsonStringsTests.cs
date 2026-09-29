public class JsonStringsTests
{
    [Test]
    [Arguments("""{"name":"value"}""", "value")]
    [Arguments("""{"other":"x","name":"value"}""", "value")]
    [Arguments("""{"other":{"name":"nested"},"name":"value"}""", "value")]
    [Arguments("""{"name":"escaped name"}""", "escaped name")]
    [Arguments("""{"name":"escaped \"value\""}""", "escaped \"value\"")]
    [Arguments("""{"name":5,"name":"the first that is text"}""", "the first that is text")]
    [Arguments("""{"name":"first","name":"second"}""", "first")]
    [Arguments("""{"name":"found before","broken":""", "found before")]
    [Arguments("""{"Name":"value"}""", null)]
    [Arguments("""{"name":5}""", null)]
    [Arguments("""{"name":null}""", null)]
    [Arguments("""{"other":"value"}""", null)]
    [Arguments("""["name","value"]""", null)]
    [Arguments("\"name\"", null)]
    [Arguments("""{"other":"x",""", null)]
    [Arguments("not json", null)]
    [Arguments("", null)]
    public async Task FindsTheFirstStringOfTheName(string json, string? expected) =>
        await Assert.That(JsonStrings.Find(Encoding.UTF8.GetBytes(json), "name", StringComparison.Ordinal)).IsEqualTo(expected);

    [Test]
    [Arguments("""{"NAME":"value"}""", "value")]
    [Arguments("""{"NAme":"value"}""", "value")]
    [Arguments("""{"names":"x","NaMe":"value"}""", "value")]
    [Arguments("""{"nam":"value"}""", null)]
    public async Task IgnoringCase(string json, string? expected) =>
        await Assert.That(JsonStrings.Find(json, "name", StringComparison.OrdinalIgnoreCase)).IsEqualTo(expected);

    /// <summary>
    /// A name longer than the stack buffer is compared as a string instead.
    /// </summary>
    [Test]
    public async Task ALongNameIgnoringCase()
    {
        var name = new string('n', 300);
        var json = $$"""{"{{name.ToUpperInvariant()}}":"value"}""";
        await Assert.That(JsonStrings.Find(json, name, StringComparison.OrdinalIgnoreCase)).IsEqualTo("value");
    }
}
