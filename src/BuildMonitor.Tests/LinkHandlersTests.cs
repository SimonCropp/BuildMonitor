public class LinkHandlersTests
{
    [Test]
    public async Task NothingOpensAMadeUpScheme() =>
        await Assert.That(LinkHandlers.Exists("buildmonitor-no-such-scheme")).IsFalse();

    // Windows' own Settings app takes ms-settings: links on every Windows 10 and 11, and, like the
    // Claude app, is a packaged app, which has a name but no command.
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task APackagedAppIsFoundOnWindows() =>
        await Assert.That(LinkHandlers.Exists("ms-settings")).IsTrue();

    // Every Mac has an app for https links. A Launch Services call that failed to bind would be
    // caught and read as no app, so this is what tells the binding works.
    [Test]
    [RunOn(TUnit.Core.Enums.OS.MacOs)]
    public async Task ABrowserIsFoundOnMac() =>
        await Assert.That(LinkHandlers.Exists("https")).IsTrue();
}
