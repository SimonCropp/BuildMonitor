public class ScreenPayloadTests
{
    [Test]
    public Task Builds()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now));
        return Verify(payload.Describe())
            .Snapshot(
                """
                page: 0 rows: 6 details: 5 chips: 16 fields: 0 options: 0 buttons: 4 menu: 0 tray: 8 strings: 659 bytes
                row status=1 flags=0 progress=0.20 'build-all' 'Build all main' provider='jenkins' '04:00 left' chips=Build:Build,Cancel:Cancel
                row status=1 flags=0 progress=0.40 'Deploy Web' '' provider='octopus' '02:15 left' chips=Build:Build,Cancel:Cancel
                row status=1 flags=1 progress=0.50 'DiffEngine' 'test.yml main' provider='github' '03:00 left' chips=Build:Build,Branch:Branch,Cancel:Cancel
                row status=0 flags=0 progress=-1.00 'nightly' '' provider='jenkins' 'queued' chips=Build:Build,Cancel:Cancel
                row status=3 flags=0 progress=-1.00 'Verify' 'test.yml feature/inline' provider='github' '25m ago' chips=Build:Build,Branch:Branch,PullRequest:PR 42,Retry:Retry,CopyLog:Copy log
                row status=2 flags=0 progress=-1.00 'DiffEngine' 'docs.yml main' provider='github' '23h ago' chips=Build:Build,Branch:Branch
                button flags=1 'Refresh'
                button flags=1 'Options'
                button flags=1 'Filters'
                button flags=1 'Hide'
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
    public Task Form()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.ConnectionNew(), Fixtures.Now));
        return Verify(payload.Describe())
            .Snapshot(
                """
                page: 1 rows: 0 details: 0 chips: 0 fields: 5 options: 10 buttons: 4 menu: 0 tray: 8 strings: 529 bytes
                field kind=5 flags=1 'provider' 'Provider' 'AppVeyor' options=0+10
                field kind=2 flags=1 'name' 'Name' '' options=10+0
                field kind=2 flags=1 'scope:account' 'Account (optional)' '' options=10+0
                field kind=3 flags=1 'token' 'API token' '' options=10+0
                field kind=7 flags=1 'tokenHelp' 'How to get an API token' 'https://ci.appveyor.com/api-keys' options=10+0
                button flags=0 'Sign in'
                button flags=1 'Test'
                button flags=1 'Save'
                button flags=1 'Cancel'
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
    public Task DbusMenuLayout()
    {
        var tray = ScreenBuilder.Tray(Fixtures.WithBuilds());
        var layout = global::DbusMenuLayout.Build(tray.Items);
        return Verify(layout.All.Select(_ => new { _.Id, _.ItemId, _.Label, _.Enabled, _.Separator, _.Icon, Properties = _.PropertyNames }));
    }
}
