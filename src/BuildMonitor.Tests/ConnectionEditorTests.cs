/// <summary>
/// What separates the two connection editors. They were one page told apart by a nullable id, so
/// the edit page carried a provider drop down that was only disabled, and the transitions behind it
/// could still act on a value no head was meant to be able to send.
/// </summary>
public class ConnectionEditorTests
{
    [Test]
    public async Task AddingChoosesTheProvider()
    {
        var provider = Provider(Fixtures.ConnectionNew());
        await Assert.That(provider.Kind).IsEqualTo(FieldKind.Select);
        await Assert.That(provider.Options!.Count).IsEqualTo(ProviderDescriptors.All.Count);
    }

    /// <summary>
    /// Text, not a drop down that refuses to open: an existing connection's provider is not a
    /// choice being withheld, and nothing in the page can report an edit to it.
    /// </summary>
    [Test]
    public async Task EditingShowsTheProviderAsText()
    {
        var provider = Provider(Fixtures.ConnectionEdit());
        await Assert.That(provider.Kind).IsEqualTo(FieldKind.Label);
        await Assert.That(provider.Value).IsEqualTo("Jenkins");
        await Assert.That(provider.Options).IsNull();
    }

    /// <summary>
    /// A provider change on the edit page is not a change of provider. On the add page it re-derives
    /// the server, the sign in method and the scopes, which on an existing connection would describe
    /// a different service under that connection's id and stored credential.
    /// </summary>
    [Test]
    public async Task EditingIgnoresAProviderChange()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionEdit(), FormFields.Provider, "GitHub Actions");
        await Assert.That(Fixtures.ConnectionForm(state).Descriptor.Id).IsEqualTo("jenkins");
        await Assert.That(state.Form!.Value(FormFields.Server)).IsEqualTo("https://jenkins.example.com");
        await Assert.That(ConnectionDraft.Build(Fixtures.ConnectionForm(state)).ProviderId).IsEqualTo("jenkins");
    }

    [Test]
    public async Task AddingRederivesTheFieldsAProviderChanges()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GitHub Actions");
        await Assert.That(Fixtures.ConnectionForm(state).Descriptor.Id).IsEqualTo("github");
    }

    /// <summary>
    /// A poll can drop the connection between the frame being drawn and the click on its row. The
    /// editor used to open blank on an unknown id, which read as the click having opened the wrong
    /// page and offered to create a connection nobody asked for.
    /// </summary>
    [Test]
    public async Task EditingSomethingGoneOpensNothing()
    {
        var state = Fixtures.WithBuilds();
        await Assert.That(MonitorSession.OpenEditConnection(state, "gone")).IsSameReferenceAs(state);
    }

    [Test]
    public async Task AddingKeepsTheDraftIdAndEditingTheConnectionId()
    {
        await Assert.That(Fixtures.ConnectionForm(Fixtures.ConnectionNew()).ConnectionId).IsEqualTo("draft1");
        await Assert.That(Fixtures.ConnectionForm(Fixtures.ConnectionEdit()).ConnectionId).IsEqualTo(Fixtures.Jenkins.Id);
    }

    /// <summary>
    /// Remove belongs to the page that has something to remove. It was offered by whichever page
    /// had a non null editing id, and read that id back out again to do the work.
    /// </summary>
    [Test]
    public async Task OnlyEditingOffersRemove()
    {
        await Assert.That(Commands(Fixtures.ConnectionNew())).DoesNotContain(CommandKind.RemoveConnection);
        await Assert.That(Commands(Fixtures.ConnectionEdit())).Contains(CommandKind.RemoveConnection);
    }

    static Field Provider(SessionState state) =>
        ScreenBuilder.Build(state, Fixtures.Now).Form!.Fields.Single(_ => _.Id == FormFields.Provider);

    static List<CommandKind> Commands(SessionState state) =>
        ScreenBuilder.Buttons(state).Select(_ => _.Command).ToList();
}
