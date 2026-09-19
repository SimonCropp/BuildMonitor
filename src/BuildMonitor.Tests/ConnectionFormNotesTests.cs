/// <summary>
/// The two notes on the connection page belong to one method each. The token note names a scope to
/// tick while creating a token, and the sign in note names the accounts a flow turns away, so either
/// one shown under the other method reads as work still to be done before the credential would work.
/// </summary>
public class ConnectionFormNotesTests
{
    [Test]
    [Arguments("GitHub Actions")]
    [Arguments("GitLab CI")]
    [Arguments("Azure DevOps")]
    public async Task TheTokenNoteIsOnlyForTheTokenMethod(string name)
    {
        await Assert.That(Fields(name, AuthMethod.Token)).Contains(FormFields.TokenNote);
        await Assert.That(Fields(name, AuthMethod.Browser)).DoesNotContain(FormFields.TokenNote);
        await Assert.That(Fields(name, AuthMethod.Device)).DoesNotContain(FormFields.TokenNote);
    }

    [Test]
    public async Task TheSignInNoteIsOnlyForTheSignInMethods()
    {
        await Assert.That(Fields("Azure DevOps", AuthMethod.Browser)).Contains(FormFields.SignInNote);
        await Assert.That(Fields("Azure DevOps", AuthMethod.Device)).Contains(FormFields.SignInNote);
        await Assert.That(Fields("Azure DevOps", AuthMethod.Token)).DoesNotContain(FormFields.SignInNote);
    }

    /// <summary>
    /// The providers the two tests above enumerate by name, so a provider that grows a sign in flow
    /// fails here rather than quietly going untested.
    /// </summary>
    [Test]
    public async Task ThoseAreEveryProviderWithASignInFlow()
    {
        var withFlows = ProviderDescriptors.All
            .Where(_ => _.BrowserSignIn || _.DeviceSignIn)
            .Select(_ => _.Name);
        await Assert.That(withFlows).IsEquivalentTo(["GitHub Actions", "Azure DevOps", "GitLab CI"]);
    }

    /// <summary>
    /// A provider with no sign in flow has nowhere to hide the token note, so it keeps it whatever
    /// happens above. Jenkins also proves the {server} substitution survived the rename.
    /// </summary>
    [Test]
    public async Task ATokenOnlyProviderKeepsItsNote()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Jenkins");
        state = MonitorSession.FieldChanged(state, FormFields.Server, "https://ci.example.com");
        var note = Form(state).Fields.Single(_ => _.Id == FormFields.TokenNote);
        await Assert.That(note.Value).IsEqualTo("Create the token at https://ci.example.com/me/security.");
    }

    static IReadOnlyList<string> Fields(string name, AuthMethod method)
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, name);
        state = MonitorSession.FieldChanged(state, FormFields.Auth, method.ToString());
        return [..Form(state).Fields.Select(_ => _.Id)];
    }

    static FormPage Form(SessionState state) =>
        ScreenBuilder.Build(state, Fixtures.Now).Form!;
}
