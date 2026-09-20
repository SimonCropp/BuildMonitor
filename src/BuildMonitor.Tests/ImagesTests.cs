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

    /// <summary>
    /// At the size each head draws a menu item's glyph: 16 pixels on Windows and Linux, 32 on macOS.
    /// </summary>
    [Test]
    public async Task MenuGlyphsExist()
    {
        foreach (var name in TrayMenu.Glyphs)
        {
            await Assert.That(Images.Glyph(name, 16)).IsNotNull();
            await Assert.That(Images.Glyph(name, 32)).IsNotNull();
        }
    }

    /// <summary>
    /// The host marks and the provider logos are written by IconBuilder, which nothing in a build
    /// runs, from a list of its own. A mark named here and not there is a row with a gap where its
    /// picture should be, which no other test would notice.
    /// </summary>
    [Test]
    public async Task EveryHostAndProviderMarkExists()
    {
        foreach (var name in RepoHosts.All.Concat(ProviderDescriptors.All.Select(_ => $"provider-{_.Id}")))
        {
            await Assert.That(Images.Glyph(name, 16)).IsNotNull();
            await Assert.That(Images.Glyph(name, 32)).IsNotNull();
        }
    }

    /// <summary>
    /// The macOS head draws only the glyphs it was handed at start. The code directory adds the
    /// one item the menu otherwise leaves out.
    /// </summary>
    [Test]
    public async Task TheMenuCarriesOnlyTheGlyphsHandedOver()
    {
        var names = ScreenBuilder.Tray(Fixtures.WithCodeDirectory()).Items.Select(_ => _.IconName ?? "");
        await Assert.That(names).IsEquivalentTo(TrayMenu.Glyphs);
    }

    [Test]
    public async Task MissingIsNull() =>
        await Assert.That(Images.Bytes("nope.png")).IsNull();
}
