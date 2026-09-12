/// <summary>
/// The names secrets are stored under. Keyed by connection id, so renaming a connection keeps
/// its token and removing one knows what to delete.
/// </summary>
static class SecretKeys
{
    public static string Token(string connectionId) =>
        $"connection:{connectionId}";

    public static string Refresh(string connectionId) =>
        $"connection:{connectionId}:refresh";
}
