public class VersionReaderTests
{
    [Test]
    public async Task IsPinnedForTests() =>
        await Assert.That(VersionReader.VersionString).IsEqualTo("TheVersion");
}
