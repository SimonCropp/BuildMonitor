/// <summary>
/// A store in memory that counts reads, and can act while a read is out at it, as another thread
/// would.
/// </summary>
sealed class CountingSecretStore : ISecretStore
{
    MemorySecretStore values = new();

    public int Reads { get; private set; }

    // Runs once, after the read has taken its value and before it returns.
    public Action? DuringRead { get; set; }

    public string? Read(string key)
    {
        Reads++;
        var value = values.Read(key);
        var during = DuringRead;
        DuringRead = null;
        during?.Invoke();
        return value;
    }

    public void Write(string key, string value) =>
        values.Write(key, value);

    public void Delete(string key) =>
        values.Delete(key);
}
