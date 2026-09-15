/// <summary>
/// Keeps each secret in memory after the first read. The poller reads a connection's token at the
/// start of every cycle, before it knows whether anything is due, and on macOS, and on Linux with
/// the Secret Service, every read ran a process: /usr/bin/security or secret-tool. Writes and
/// deletes reach the store before memory, and everything in the process writes through the one
/// instance, so memory cannot go stale in process.
/// </summary>
sealed class CachingSecretStore(ISecretStore inner) : ISecretStore
{
    Lock gate = new();
    // Two writes to one key land in memory in the order they reached the store.
    Lock writing = new();
    Dictionary<string, string> values = new();
    // Counts writes and deletes, so a read that was out at the store while one landed is not kept:
    // it may hold the value that write replaced.
    long changes;

    public string? Read(string key)
    {
        long seen;
        lock (gate)
        {
            if (values.TryGetValue(key, out var value))
            {
                return value;
            }

            seen = changes;
        }

        // Outside the lock, since a read can wait on the user, as when the Keychain asks whether to
        // allow access, and every other connection's read would wait with it.
        var read = inner.Read(key);
        // A miss is not kept: a connection with no token waits for a wake rather than cycling, and
        // signing in writes through here.
        if (read is null)
        {
            return null;
        }

        lock (gate)
        {
            if (changes == seen)
            {
                values[key] = read;
            }
        }

        return read;
    }

    public void Write(string key, string value)
    {
        lock (writing)
        {
            inner.Write(key, value);
            lock (gate)
            {
                changes++;
                values[key] = value;
            }
        }
    }

    public void Delete(string key)
    {
        lock (writing)
        {
            inner.Delete(key);
            lock (gate)
            {
                changes++;
                values.Remove(key);
            }
        }
    }
}
