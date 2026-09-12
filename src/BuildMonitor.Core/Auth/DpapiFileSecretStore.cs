/// <summary>
/// A file per secret, encrypted with the Windows Data Protection API for the current user, so
/// another account on the machine reads noise.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class DpapiFileSecretStore(string directory) : FileSecretStore(directory)
{
    static readonly byte[] entropy = "BuildMonitor"u8.ToArray();

    protected override string Extension => ".bin";

    protected override byte[] Encode(string value) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser);

    protected override string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser));
}
