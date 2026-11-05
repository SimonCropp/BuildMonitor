public class ScreenPayloadTests
{
    [Test]
    public Task Builds()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now));
        return Verify(payload.Describe());
    }

    /// <summary>
    /// A lane reaches a native head as a build's row with no flags, no name and no marks: nothing
    /// a head draws is new, so the committed renderers draw it as they are.
    /// </summary>
    [Test]
    public Task Lanes()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithLanes(), Fixtures.Now));
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
                page: 1 rows: 0 details: 0 chips: 0 spans: 0 fields: 6 options: 10 buttons: 4 menu: 0 strings: 511 bytes
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

                """);
    }

    /// <summary>
    /// One entry per item, lines or not: a line is a flag on the item below it, so the index a
    /// head reports for a click is still the index of the command behind it.
    /// </summary>
    [Test]
    public async Task AMenusLinesAreFlagsOnItsItems()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.WithMenu(), Fixtures.Now));
        var menu = payload.Describe()
            .Split('\n')
            .Select(_ => _.TrimEnd('\r'))
            .Where(_ => _.StartsWith("menu flags="));
        await Assert.That(string.Join('\n', menu)).IsEqualTo(
            """
            menu flags=0 'Open build'
            menu flags=0 'Open branch'
            menu flags=0 'Copy build URL'
            menu flags=1 'Cancel build'
            menu flags=1 'Refresh'
            menu flags=0 'Group by org: VerifyTests'
            menu flags=1 'Exclude action: test.yml'
            menu flags=0 'Exclude branch: main'
            menu flags=0 'Exclude repo: VerifyTests/DiffEngine'
            menu flags=0 'Exclude org: VerifyTests'
            """);
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
        var layout = PlatformHeads.DbusMenuLayout.Build(tray.Items, Images.Glyph);
        return Verify(layout.All.Select(_ => new { _.Id, _.ItemId, _.Label, _.Enabled, _.Separator, _.Icon, Properties = _.PropertyNames }));
    }
}
