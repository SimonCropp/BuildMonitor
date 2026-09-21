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
        return Verify(payload.Describe())
            .Snapshot(
                """
                page: 1 rows: 0 details: 0 chips: 0 spans: 0 fields: 6 options: 10 buttons: 4 menu: 0 tray: 8 strings: 693 bytes
                search='' tip='' empty=''
                status='Polled 5s ago' tip=''
                field kind=5 flags=1 'provider' 'Provider' 'AppVeyor' options=0+10
                field kind=2 flags=1 'name' 'Name' '' options=10+0
                field kind=2 flags=1 'scope:account' 'Account (optional)' '' options=10+0
                field kind=3 flags=1 'token' 'API token' '' options=10+0
                field kind=7 flags=1 'tokenHelp' 'How to get an API token' 'https://ci.appveyor.com/api-keys' options=10+0
                field kind=7 flags=1 'providerDocs' 'AppVeyor documentation' 'https://github.com/SimonCropp/BuildMonitor/blob/main/docs/providers/appveyor.md' options=10+0
                button flags=0 'Sign in' tip=''
                button flags=1 'Test' tip='Check the server and credential without saving'
                button flags=1 'Save' tip=''
                button flags=1 'Cancel' tip=''
                tray flags=1 'open' 'Open' icon='open'
                tray flags=1 'refresh' 'Refresh' icon='refresh'
                tray flags=1 'options' 'Options' icon='options'
                tray flags=1 'filters' 'Filters' icon='filters'
                tray flags=1 'logs' 'Open logs' icon='logs'
                tray flags=1 'issue' 'Raise issue' icon='issue'
                tray flags=1 'update' 'Update' icon='update'
                tray flags=1 'exit' 'Exit' icon='exit'

                """);
    }

    [Test]
    public async Task IdsFollowTheFlattening()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now));
        await Assert.That(payload.TrayItemIds.Last()).IsEqualTo(TrayMenu.Exit);
        await Assert.That(payload.TrayItemIds.First()).IsEqualTo(TrayMenu.Open);
    }

    [Test]
    public async Task EachBuildIsANewGeneration()
    {
        // A native head draws only a generation it has not drawn, so a rebuild that kept the last
        // one would leave the change off the screen.
        var payload = new ScreenPayload();
        var screen = ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now);
        payload.Build(screen);
        var first = payload.Generation;
        payload.Build(screen);
        await Assert.That(payload.Generation).IsNotEqualTo(first);
    }

    [Test]
    public Task DbusMenuLayout()
    {
        var tray = ScreenBuilder.Tray(Fixtures.WithBuilds());
        var layout = global::DbusMenuLayout.Build(tray.Items);
        return Verify(layout.All.Select(_ => new { _.Id, _.ItemId, _.Label, _.Enabled, _.Separator, _.Icon, Properties = _.PropertyNames }));
    }
}
