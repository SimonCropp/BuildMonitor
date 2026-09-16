public class SignInCoordinatorTests
{
    [Test]
    public async Task ADeviceSignInToGitLabAsksWhoItIsWithABearerToken()
    {
        // In PRIVATE-TOKEN, GitLab looks the token up only as an access token and refuses it.
        var connection = new Connection
        {
            Id = "gl",
            ProviderId = "gitlab",
            Name = "GitLab",
            Auth = AuthMethod.Device
        };
        var flowId = Guid.NewGuid();
        var editing = MonitorSession.OpenConnectionEditor(Fixtures.Connected(), null, connection.Id);
        var host = new SessionHost(MonitorSession.BeginSignIn(editing, connection, AuthMethod.Device, flowId));
        var secrets = new MemorySecretStore();
        var handler = new FakeHttpHandler()
            .Map("POST", "https://gitlab.com/oauth/authorize_device", """{"device_code":"device","user_code":"ABCD-1234","verification_uri":"https://gitlab.com/oauth/device","expires_in":900,"interval":1}""")
            .Map("POST", "https://gitlab.com/oauth/token", """{"access_token":"signed-in","refresh_token":"refresh","token_type":"Bearer"}""")
            .Get("https://gitlab.com/api/v4/user", """{"username":"simon"}""");

        new SignInCoordinator(host, secrets, handler).Start(connection, AuthMethod.Device, flowId);
        await WaitFor(() => host.State.SignIn is null);

        await Assert.That(host.State.Form!.Message).IsEqualTo("Signed in as simon.");
        await Assert.That(secrets.Read(SecretKeys.Token(connection.Id))).IsEqualTo("signed-in");
        var asked = handler.RequestHeaders[handler.Requests.IndexOf("GET https://gitlab.com/api/v4/user")];
        await Assert.That(asked.Authorization?.ToString()).IsEqualTo("Bearer signed-in");
        await Assert.That(asked.Contains("PRIVATE-TOKEN")).IsFalse();
    }

    static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException();
            }

            await Task.Delay(20);
        }
    }
}
