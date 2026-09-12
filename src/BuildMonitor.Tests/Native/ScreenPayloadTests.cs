public class ScreenPayloadTests
{
    [Test]
    public Task Builds()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now));
        return Verify(payload.Describe());
    }

    [Test]
    public Task Form()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.ConnectionNew(), Fixtures.Now));
        return Verify(payload.Describe());
    }

    [Test]
    public async Task IdsFollowTheFlattening()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now));
        await Assert.That(payload.TrayItemIds.Last()).IsEqualTo(TrayMenu.Exit);
        await Assert.That(payload.TrayItemIds.Count(_ => _.StartsWith("build:", StringComparison.Ordinal))).IsGreaterThan(0);
    }

    [Test]
    public Task DbusMenuLayout()
    {
        var tray = ScreenBuilder.Tray(Fixtures.WithBuilds());
        var layout = global::DbusMenuLayout.Build(tray.Items);
        return Verify(layout.All.Select(_ => new { _.Id, _.ItemId, _.Label, _.Enabled, _.Separator, _.Icon, Children = _.Children.Select(child => child.Id), Properties = _.PropertyNames }));
    }
}
