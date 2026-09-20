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
    /// Every picture a row can name is written by IconBuilder, which nothing in a build runs, from
    /// a list of its own. A name used here and not written there is a row with a gap where its
    /// picture should be, which no other test would notice.
    /// </summary>
    [Test]
    public async Task EveryMarkARowCanNameExists()
    {
        foreach (var name in RepoHosts.All
                     .Concat(ProviderDescriptors.All.Select(_ => $"provider-{_.Id}"))
                     .Concat(RowChips.Icons))
        {
            await Assert.That(Images.Glyph(name, 16)).IsNotNull();
            await Assert.That(Images.Glyph(name, 32)).IsNotNull();
        }
    }

    /// <summary>
    /// And the other way round: a chip drawn with a picture that the heads were never handed is an
    /// empty button, which is how Cancel shipped the first time it stopped being a word. The
    /// fixtures between them carry every chip there is.
    /// </summary>
    [Test]
    public async Task EveryChipTheFixturesDrawIsHandedOver()
    {
        var drawn = new[] { Fixtures.WithBuilds(), Fixtures.WithLocalRepos(), Fixtures.WithTwoFailures() }
            .SelectMany(_ => ScreenBuilder.Build(_, Fixtures.Now).Builds!.Rows)
            .SelectMany(_ => _.Chips)
            .Select(_ => _.Icon)
            .Where(_ => _.Length > 0)
            .Distinct()
            .ToList();
        await Assert.That(drawn).IsNotEmpty();
        foreach (var icon in drawn)
        {
            await Assert.That(RowChips.Icons).Contains(icon);
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
