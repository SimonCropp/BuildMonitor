public class ProtocolTests
{
    [Test]
    public async Task MessageRoundTrip()
    {
        var message = new Message(Verb.Open, "gh/DiffEngine/test.yml/main", "line one\nline: two");
        var text = message.Build();
        await Assert.That(Message.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(message);
        await Verify(Encoding.UTF8.GetString(text))
            .Snapshot(
                """
                version: 1
                verb: open
                key: Z2gvRGlmZkVuZ2luZS90ZXN0LnltbC9tYWlu
                body: bGluZSBvbmUKbGluZTogdHdv


                """);
    }

    [Test]
    public async Task ResponseRoundTrip()
    {
        var response = Response.Error("No build with key x");
        await Assert.That(Response.TryParse(response.Build(), out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(response);
    }

    /// <summary>
    /// JSON goes out as the UTF-8 the serializer wrote and is read back from the UTF-8 that
    /// arrived, and reads the same as text either side: a sent listing and one received are equal.
    /// </summary>
    [Test]
    public async Task JsonResponseRoundTrip()
    {
        var connections = new List<ConnectionDto>
        {
            new("github", "GitHub, née \"Actions\"", "github", "Connected", null, null, 3)
        };
        var response = Response.Success(connections, DtoContext.Default.ListConnectionDto);
        await Assert.That(Response.TryParse(response.Build(), out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(response);
        var read = parsed!.Read(DtoContext.Default.ListConnectionDto)!;
        await Assert.That(read.Single().Name).IsEqualTo("GitHub, née \"Actions\"");
        await Assert.That(parsed.Body).IsEqualTo(JsonSerializer.Serialize(connections, DtoContext.Default.ListConnectionDto));
    }

    [Test]
    public async Task UnknownLinesAreIgnoredAndUnknownVerbsRejected()
    {
        await Assert.That(Message.TryParse("version: 9\nverb: ping\nfuture: x\n\n"u8, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(Verb.Ping);
        await Assert.That(Message.TryParse("verb: dance\n\n"u8, out _)).IsFalse();
        await Assert.That(Message.TryParse("nothing"u8, out _)).IsFalse();
    }

    [Test]
    public async Task BadBase64IsEmptyNotAnException()
    {
        await Assert.That(Message.TryParse("verb: get\nkey: !!!\n\n"u8, out var message)).IsTrue();
        await Assert.That(message!.Key).IsEqualTo("");
    }
}
