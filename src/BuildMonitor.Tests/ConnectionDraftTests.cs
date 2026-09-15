public class ConnectionDraftTests
{
    [Test]
    public async Task BuildsAConnectionFromTheForm()
    {
        var state = Fixtures.ConnectionNew();
        state = MonitorSession.FieldChanged(state, FormFields.Provider, "Azure DevOps");
        state = MonitorSession.FieldChanged(state, FormFields.Name, " Work ");
        state = MonitorSession.FieldChanged(state, FormFields.Scope("organization"), "contoso");
        state = MonitorSession.FieldChanged(state, FormFields.Server, "https://dev.azure.com/");
        var connection = ConnectionDraft.Build(state.Form!);
        await Verify(connection)
            .Snapshot(
                """
                {
                  Id: draft1,
                  ProviderId: azure-devops,
                  Name: Work,
                  Server: https://dev.azure.com,
                  Scope: {
                    organization: contoso
                  },
                  Auth: Browser
                }
                """);
    }

    [Test]
    public async Task EmptyNameFallsBackToProvider()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GoCD");
        await Assert.That(ConnectionDraft.Build(state.Form!).Name).IsEqualTo("GoCD");
    }

    [Test]
    [Arguments("Jenkins", "Enter the server URL.")]
    [Arguments("Azure DevOps", "Enter the organization.")]
    [Arguments("Bitbucket Pipelines", "Enter the workspace.")]
    [Arguments("AppVeyor", "Enter the api token.")]
    [Arguments("GitHub Actions", "Sign in first, or switch to a token.")]
    public async Task ValidationMessages(string provider, string expected)
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, provider);
        await Assert.That(ConnectionDraft.Validate(state.Form!)).IsEqualTo(expected);
    }

    [Test]
    public async Task BadServerIsRejected()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Jenkins");
        state = MonitorSession.FieldChanged(state, FormFields.Server, "ftp://jenkins.local");
        await Assert.That(ConnectionDraft.Validate(state.Form!)).IsEqualTo("The server must be an http or https URL.");
    }

    [Test]
    [Arguments("octopus.example.com", "https://octopus.example.com")]
    [Arguments("octopus.example.com/", "https://octopus.example.com")]
    [Arguments("localhost:8080", "https://localhost:8080")]
    [Arguments("http://octopus.example.com", "http://octopus.example.com")]
    public async Task ServerWithoutSchemeIsHttps(string server, string expected)
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Octopus Deploy");
        state = MonitorSession.FieldChanged(state, FormFields.Server, server);
        await Assert.That(ConnectionDraft.Build(state.Form!).Server).IsEqualTo(expected);
        await Assert.That(ConnectionDraft.Validate(state.Form!)).IsNotEqualTo("The server must be an http or https URL.");
    }

    [Test]
    public async Task FailedTestClearsTestingMessage()
    {
        var state = MonitorSession.SetFormMessage(Fixtures.ConnectionNew(), "Testing...");
        state = MonitorSession.SetFormError(state, "Unauthorized");
        await Assert.That(state.Form!.Message).IsNull();
        await Assert.That(state.Form!.Error).IsEqualTo("Unauthorized");
    }

    [Test]
    public async Task CallbackPortIsOptionalAndBounded()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GitLab CI");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Browser));
        await Assert.That(ConnectionDraft.Build(state.Form!).CallbackPort).IsNull();

        state = MonitorSession.FieldChanged(state, FormFields.CallbackPort, "80");
        await Assert.That(ConnectionDraft.Validate(state.Form!)).IsEqualTo("The callback port must be between 1024 and 65535.");

        state = MonitorSession.FieldChanged(state, FormFields.CallbackPort, "8420");
        await Assert.That(ConnectionDraft.Build(state.Form!).CallbackPort).IsEqualTo(8420);
        await Assert.That(ConnectionDraft.Validate(state.Form!)).IsEqualTo("Sign in first, or switch to a token.");
    }

    [Test]
    public async Task EditingKeepsTheStoredToken()
    {
        var state = Fixtures.ConnectionEdit();
        await Assert.That(ConnectionDraft.Validate(state.Form!)).IsNull();
        await Assert.That(ConnectionDraft.Build(state.Form!).Id).IsEqualTo(Fixtures.Jenkins.Id);
    }
}
