/// <summary>
/// The freedesktop Secret Service, through libsecret's secret-tool. The value goes in on stdin,
/// which is the one channel other users cannot watch.
/// </summary>
sealed class SecretToolSecretStore : ISecretStore
{
    const string service = "BuildMonitor";

    public static bool IsAvailable() =>
        ProcessRunner.OnPath("secret-tool") is not null;

    public string? Read(string key)
    {
        var (code, output) = ProcessRunner.Run("secret-tool", ["lookup", "service", service, "key", key]);
        return code == 0 && output.Length > 0 ? output.TrimEnd('\n', '\r') : null;
    }

    public void Write(string key, string value)
    {
        var (code, output) = ProcessRunner.Run("secret-tool", ["store", $"--label={service} {key}", "service", service, "key", key], value);
        if (code != 0)
        {
            throw new InvalidOperationException($"secret-tool store failed: {output}");
        }
    }

    public void Delete(string key) =>
        ProcessRunner.Run("secret-tool", ["clear", "service", service, "key", key]);
}
