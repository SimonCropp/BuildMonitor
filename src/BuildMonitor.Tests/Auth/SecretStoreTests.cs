public class SecretStoreTests
{
    [Test]
    public async Task FileStoreRoundTrips()
    {
        var directory = TempDirectory();
        try
        {
            var store = new FileSecretStore(directory);
            store.Write("connection:a", "token one");
            store.Write("connection:a", "token two");
            await Assert.That(store.Read("connection:a")).IsEqualTo("token two");
            await Assert.That(store.Read("connection:b")).IsNull();
            await Assert.That(Directory.GetFiles(directory).Select(_ => Path.GetFileName(_))).IsEquivalentTo(["connection_a.secret"]);

            if (!OperatingSystem.IsWindows())
            {
                await Assert.That(File.GetUnixFileMode(Path.Combine(directory, "connection_a.secret"))).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            store.Delete("connection:a");
            await Assert.That(store.Read("connection:a")).IsNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task DpapiStoreRoundTripsAndDoesNotWritePlainText()
    {
        var directory = TempDirectory();
        try
        {
            var store = new DpapiFileSecretStore(directory);
            store.Write("connection:a", "hunter2");
            await Assert.That(store.Read("connection:a")).IsEqualTo("hunter2");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "connection_a.bin"));
            await Assert.That(Encoding.UTF8.GetString(bytes)).DoesNotContain("hunter2");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task PlatformStoreIsChosen()
    {
        var store = SecretStores.ForPlatform(TempDirectory());
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(store).IsTypeOf<DpapiFileSecretStore>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            await Assert.That(store).IsTypeOf<KeychainSecretStore>();
        }
        else
        {
            await Assert.That(store is SecretToolSecretStore or FileSecretStore).IsTrue();
        }
    }

    [Test]
    // Every hosted service has a compiled in id, so no user supplied one is needed.
    [Arguments("github", "", "https://github.com/login/oauth/authorize")]
    [Arguments("gitlab", "", "https://gitlab.com/oauth/authorize")]
    [Arguments("azure-devops", "", "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize")]
    [Arguments("github", "myapp", "https://github.com/login/oauth/authorize")]
    [Arguments("gitlab", "myapp", "https://gitlab.com/oauth/authorize")]
    [Arguments("azure-devops", "myapp", "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize")]
    [Arguments("jenkins", "myapp", null)]
    public async Task OAuthClientResolution(string providerId, string clientId, string? authorizeUrl)
    {
        var connection = new Connection
        {
            Id = "x",
            ProviderId = providerId,
            Name = "x",
            ClientId = clientId.Length == 0 ? null : clientId
        };
        var client = OAuthClients.For(connection);
        await Assert.That(client?.AuthorizeUrl).IsEqualTo(authorizeUrl);
    }

    [Test]
    public async Task SelfHostedGitLabUsesItsServer()
    {
        var connection = new Connection
        {
            Id = "x",
            ProviderId = "gitlab",
            Name = "x",
            Server = "https://gitlab.example.com/",
            ClientId = "app"
        };
        var client = OAuthClients.For(connection)!;
        await Assert.That(client.TokenUrl).IsEqualTo("https://gitlab.example.com/oauth/token");
        await Assert.That(client.DeviceCodeUrl).IsEqualTo("https://gitlab.example.com/oauth/authorize_device");
    }

    static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), $"BuildMonitorSecrets_{Guid.NewGuid():N}");
}
