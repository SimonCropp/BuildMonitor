public class ImagesTests
{
    [Test]
    public Task EmbeddedImages() =>
        Verify(Images.All());

    [Test]
    public async Task EveryTrayKindHasEveryFormat()
    {
        foreach (var kind in Enum.GetValues<TrayIconKind>())
        {
            await Assert.That(Images.TrayIco(kind)).IsNotNull();
            await Assert.That(Images.TrayPng(kind, 16)).IsNotNull();
            await Assert.That(Images.TrayPng(kind, 256)).IsNotNull();
            await Assert.That(Images.TrayArgb(kind, 22)!.Length).IsEqualTo(22 * 22 * 4);
        }
    }

    [Test]
    [Arguments("open")]
    [Arguments("refresh")]
    [Arguments("options")]
    [Arguments("filters")]
    [Arguments("logs")]
    [Arguments("issue")]
    [Arguments("update")]
    [Arguments("exit")]
    [Arguments("build")]
    [Arguments("retry")]
    [Arguments("cancel")]
    public async Task MenuGlyphsExist(string name) =>
        await Assert.That(Images.Glyph(name, 16)).IsNotNull();

    [Test]
    public async Task MissingIsNull() =>
        await Assert.That(Images.Bytes("nope.png")).IsNull();
}
