public class ProtocolTests
{
    [Test]
    public async Task MessageRoundTrip()
    {
        var message = new Message(Verb.Open, "gh/DiffEngine/test.yml/main", "line one\nline: two");
        var text = message.Build();
        await Assert.That(Message.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(message);
        await Verify(text)
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

    [Test]
    public async Task UnknownLinesAreIgnoredAndUnknownVerbsRejected()
    {
        await Assert.That(Message.TryParse("version: 9\nverb: ping\nfuture: x\n\n", out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(Verb.Ping);
        await Assert.That(Message.TryParse("verb: dance\n\n", out _)).IsFalse();
        await Assert.That(Message.TryParse("nothing", out _)).IsFalse();
    }

    [Test]
    public async Task BadBase64IsEmptyNotAnException()
    {
        await Assert.That(Message.TryParse("verb: get\nkey: !!!\n\n", out var message)).IsTrue();
        await Assert.That(message!.Key).IsEqualTo("");
    }
}
