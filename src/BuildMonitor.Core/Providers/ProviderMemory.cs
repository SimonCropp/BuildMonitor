/// <summary>
/// What a provider keeps about one connection from one poll to the next, such as that its server
/// has no GraphQL. A provider is shared by every connection and its client lives for one poll, so
/// without this each poll paid again to learn what the one before already had. It belongs to the
/// poller, like the <see cref="ETagCache"/>; a one-off call such as a retry starts with nothing.
/// </summary>
sealed class ProviderMemory
{
    ConcurrentDictionary<string, object> values = new();

    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (values.TryGetValue(key, out var found) &&
            found is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public void Set(string key, object value) =>
        values[key] = value;

    public void Remove(string key) =>
        values.TryRemove(key, out _);
}
