/// <summary>
/// The override a pipeline writes to say who a run is really for, where that is not whoever
/// started it: an end to end suite a service account runs for whoever committed the code names
/// that person rather than the account. Nothing a service writes itself, so a run only carries one
/// on purpose, and one that does not is named as it was before.
/// <para>
/// Each service keeps it wherever it keeps what a pipeline writes: Azure DevOps in a build
/// property, TeamCity and Jenkins in a build parameter. Octopus is the exception, as a deployment
/// has nowhere for one and its release notes carry the id under their own name.
/// </para>
/// </summary>
static class TriggeredBy
{
    public const string Property = "TriggeredBy";

    /// <summary>
    /// Who the value names: the name as written, or, where it is a guid, whoever
    /// <see cref="IdentityNames"/> says that id is. Null where it names nobody, because there is
    /// no value, or it is an id nothing has named yet, and then the row keeps the name the service
    /// gave it. <paramref name="run"/> names the run in the log, which is the only place a run
    /// that fell back says so.
    /// </summary>
    public static string? Author(ProviderContext context, string? value, string run)
    {
        if (value is null)
        {
            return null;
        }

        value = value.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (!Guid.TryParse(value, out _))
        {
            Log.Debug("{Run} is for {Author}, as its {Property} says", run, value, Property);
            return value;
        }

        if (context.Identities.Name(value) is { } name)
        {
            Log.Debug("{Run} is for {Author}, as the {Property} id {Identity} names", run, name, Property, value);
            return name;
        }

        Log.Debug("{Run} holds {Property} id {Identity}, which nothing has named", run, Property, value);
        return null;
    }
}
