public class McpHostTests
{
    /// <summary>
    /// Each icon is the embedded PNG of the size it claims. A size IconBuilder stopped writing is
    /// left out without an error, so this is what notices the connector going back to a letter
    /// tile.
    /// </summary>
    [Test]
    public async Task ServerInfoCarriesTheTrayIcon()
    {
        var icons = McpHost.ServerInfo.Icons!;
        await Assert.That(icons.Select(_ => _.Sizes!.Single())).IsEquivalentTo(["48x48", "256x256"]);
        foreach (var icon in icons)
        {
            await Assert.That(icon.MimeType).IsEqualTo("image/png");
            var size = int.Parse(icon.Sizes!.Single().Split('x')[0]);
            var prefix = "data:image/png;base64,";
            await Assert.That(icon.Source).StartsWith(prefix);
            var bytes = Convert.FromBase64String(icon.Source[prefix.Length..]);
            await Assert.That(bytes).IsEquivalentTo(Images.TrayPng(TrayIconKind.Idle, size)!);
        }
    }
}
