/// <summary>
/// The status shown when a retry or cancel fails. A connection that polls fine but is refused on
/// a change lacks the permission to change builds, and "401 Unauthorized" alone reads as a dead
/// token, so a refusal says what is missing and, where the provider documents it, what it needs.
/// </summary>
static class ActionFailure
{
    public static string Describe(SessionState state, string? connectionId, string what, Exception exception)
    {
        var connection = state.Settings.Connections.FirstOrDefault(_ => _.Id == connectionId);
        var descriptor = connection is null ? null : ProviderDescriptors.Get(connection.ProviderId);
        return Describe(descriptor, what, exception);
    }

    public static string Describe(ProviderDescriptor? descriptor, string what, Exception exception)
    {
        if (exception is not AuthException ||
            descriptor is null)
        {
            return $"{what} failed: {exception.Message}";
        }

        if (descriptor.ActionPermission is null)
        {
            return $"{what} failed: {exception.Message}. The connection may not be allowed to change builds";
        }

        return $"{what} failed: the connection can watch builds but not change them. {descriptor.Name} needs {descriptor.ActionPermission}";
    }

    public static string DescribeRunNext(SessionState state, string? connectionId, string what, Exception exception)
    {
        var connection = state.Settings.Connections.FirstOrDefault(_ => _.Id == connectionId);
        var descriptor = connection is null ? null : ProviderDescriptors.Get(connection.ProviderId);
        return DescribeRunNext(descriptor, what, exception);
    }

    /// <summary>
    /// Where reordering the queue is a permission of its own, a connection that retries and
    /// cancels can still be refused it, so the refusal names that permission rather than claiming
    /// the connection can change nothing.
    /// </summary>
    public static string DescribeRunNext(ProviderDescriptor? descriptor, string what, Exception exception)
    {
        if (exception is AuthException &&
            descriptor?.QueuePermission is { } needed)
        {
            return $"{what} failed: {descriptor.Name} needs {needed}";
        }

        return Describe(descriptor, what, exception);
    }
}
