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
        var connection = ConnectionDraft.Build(Fixtures.ConnectionForm(state));
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
        await Assert.That(ConnectionDraft.Build(Fixtures.ConnectionForm(state)).Name).IsEqualTo("GoCD");
    }

    [Test]
    [Arguments("Jenkins", "Enter the server URL.", FormFields.Server)]
    [Arguments("Azure DevOps", "Organization is required.", "scope:organization")]
    [Arguments("Bitbucket Pipelines", "Workspace is required.", "scope:workspace")]
    [Arguments("AppVeyor", "API token is required.", FormFields.Token)]
    [Arguments("GitHub Actions", "Sign in first, or switch to a token.", FormFields.Auth)]
    public async Task ValidationMessages(string provider, string expected, string field)
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, provider);
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))).IsEqualTo(new FormError(expected, field));
    }

    /// <summary>
    /// A label keeps the case it is drawn with: lowercasing it made "API token" read as "api token",
    /// and lowering only a first letter would do the same to "Atlassian".
    /// </summary>
    [Test]
    public async Task MissingUserKeepsTheLabelsCase()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Bitbucket Pipelines");
        state = MonitorSession.FieldChanged(state, FormFields.Scope("workspace"), "contoso");
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))).IsEqualTo(new FormError("Atlassian account email is required.", FormFields.User));
    }

    [Test]
    public async Task BadServerIsRejected()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Jenkins");
        state = MonitorSession.FieldChanged(state, FormFields.Server, "ftp://jenkins.local");
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))).IsEqualTo(new FormError("The server must be an http or https URL.", FormFields.Server));
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
        await Assert.That(ConnectionDraft.Build(Fixtures.ConnectionForm(state)).Server).IsEqualTo(expected);
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))?.Text).IsNotEqualTo("The server must be an http or https URL.");
    }

    [Test]
    public async Task FailedTestClearsTestingMessage()
    {
        var state = MonitorSession.SetFormMessage(Fixtures.ConnectionNew(), "Testing...");
        state = MonitorSession.SetFormError(state, new("Unauthorized"));
        await Assert.That(Fixtures.ConnectionForm(state).Message).IsNull();
        await Assert.That(state.Form!.Error).IsEqualTo(new FormError("Unauthorized"));
    }

    [Test]
    public async Task CallbackPortIsOptionalAndBounded()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GitLab CI");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Browser));
        await Assert.That(ConnectionDraft.Build(Fixtures.ConnectionForm(state)).CallbackPort).IsNull();

        state = MonitorSession.FieldChanged(state, FormFields.CallbackPort, "80");
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))).IsEqualTo(new FormError("The callback port must be between 1024 and 65535.", FormFields.CallbackPort));

        state = MonitorSession.FieldChanged(state, FormFields.CallbackPort, "8420");
        await Assert.That(ConnectionDraft.Build(Fixtures.ConnectionForm(state)).CallbackPort).IsEqualTo(8420);
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))).IsEqualTo(new FormError("Sign in first, or switch to a token.", FormFields.Auth));
    }

    [Test]
    public async Task EditingKeepsTheStoredToken()
    {
        var state = Fixtures.ConnectionEdit();
        await Assert.That(ConnectionDraft.Validate(Fixtures.ConnectionForm(state))).IsNull();
        await Assert.That(ConnectionDraft.Build(Fixtures.ConnectionForm(state)).Id).IsEqualTo(Fixtures.Jenkins.Id);
    }
}
