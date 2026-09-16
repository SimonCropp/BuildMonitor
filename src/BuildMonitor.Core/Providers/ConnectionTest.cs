/// <summary>
/// What a connection test found: whether the credential works, who it belongs to, and what it may
/// do to builds where the service says.
/// </summary>
record ConnectionTest(bool Ok, string Message, BuildAccess Access = BuildAccess.Unknown)
{
    /// <summary>
    /// The message the connection editor shows. A connection that can only watch says so and names
    /// what it lacks, because its rows offer no retry or cancel and nothing else on screen says why.
    /// </summary>
    public string Describe(ProviderDescriptor descriptor)
    {
        if (!Ok)
        {
            return Message;
        }

        switch (Access)
        {
            case BuildAccess.Watch when descriptor.ActionPermission is { } needed:
                return $"{Message}. The connection can watch builds but not change them. {descriptor.Name} needs {needed}";
            case BuildAccess.Watch:
                return $"{Message}. The connection can watch builds but not change them";
            case BuildAccess.Change:
                return $"{Message}. The connection can watch and change builds";
            default:
                return Message;
        }
    }
}
