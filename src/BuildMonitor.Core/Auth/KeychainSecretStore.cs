/// <summary>
/// The login keychain, through the security command that ships with macOS. A generic password
/// per key under one service name. The value travels on the command line for a store, which
/// ps can see for the milliseconds it takes; the alternative is linking Security.framework,
/// which is not worth it for a token that is about to be sent over the network anyway.
/// </summary>
sealed class KeychainSecretStore : ISecretStore
{
    const string service = "BuildMonitor";

    public string? Read(string key)
    {
        var (code, output) = ProcessRunner.Run("/usr/bin/security", ["find-generic-password", "-s", service, "-a", key, "-w"]);
        return code == 0 ? output.TrimEnd('\n', '\r') : null;
    }

    public void Write(string key, string value)
    {
        var (code, output) = ProcessRunner.Run("/usr/bin/security", ["add-generic-password", "-U", "-s", service, "-a", key, "-l", $"{service} {key}", "-w", value]);
        if (code != 0)
        {
            throw new InvalidOperationException($"security add-generic-password failed: {output}");
        }
    }

    public void Delete(string key) =>
        ProcessRunner.Run("/usr/bin/security", ["delete-generic-password", "-s", service, "-a", key]);
}
