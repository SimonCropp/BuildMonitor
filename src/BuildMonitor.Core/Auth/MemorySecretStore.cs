/// <summary>
/// For tests and for the sign in page's draft, which has no connection saved yet.
/// </summary>
sealed class MemorySecretStore : ISecretStore
{
    ConcurrentDictionary<string, string> values = new();

    public string? Read(string key) =>
        values.GetValueOrDefault(key);

    public void Write(string key, string value) =>
        values[key] = value;

    public void Delete(string key) =>
        values.TryRemove(key, out _);

    public IReadOnlyDictionary<string, string> All => values;
}
