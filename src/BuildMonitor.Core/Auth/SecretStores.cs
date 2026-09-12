/// <summary>
/// The platform's own store where there is one: DPAPI on Windows, the Keychain on macOS, the
/// Secret Service on Linux when secret-tool is installed, else a file only the user can read.
/// The same split gh and Git Credential Manager settle on.
/// </summary>
static class SecretStores
{
    public static ISecretStore ForPlatform(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiFileSecretStore(directory);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new KeychainSecretStore();
        }

        if (SecretToolSecretStore.IsAvailable())
        {
            return new SecretToolSecretStore();
        }

        return new FileSecretStore(directory);
    }
}
