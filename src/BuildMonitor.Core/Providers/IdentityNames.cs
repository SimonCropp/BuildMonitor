/// <summary>
/// What each user id is called, across every connection. One service hands another an id and no
/// name: an Octopus release a pipeline created carries the id of whoever the Azure DevOps build was
/// for, and nothing in Octopus can turn that into a name, while every Azure DevOps build names the
/// identity it was queued for, id and all. So a provider that knows a name leaves it here and one
/// that only has an id reads it, rather than paying for a lookup that also needs rights the token
/// may not carry.
/// <para>
/// Held by the poller, shared by its connections, and never persisted: a name arrives with the
/// first build that carries it, so a fresh run learns them again within a poll or two. Until it
/// does, and for anyone who has queued nothing, an id names nobody and the row falls back.
/// </para>
/// </summary>
sealed class IdentityNames
{
    ConcurrentDictionary<Guid, string> names = new();

    /// <summary>
    /// Learns what an id is called. Ids are held as <see cref="Guid"/> so that the two services'
    /// spellings of the same one, which differ in case, are the one entry.
    /// </summary>
    public void Add(string? id, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !Guid.TryParse(id, out var parsed))
        {
            return;
        }

        names[parsed] = name.Trim();
    }

    /// <summary>
    /// What the id is called, or null where it is not an id, or nothing has named it yet.
    /// </summary>
    public string? Name(string? id)
    {
        if (!Guid.TryParse(id, out var parsed) ||
            !names.TryGetValue(parsed, out var name))
        {
            return null;
        }

        return name;
    }
}
