/// <summary>
/// Which connections can say what became of a failed branch: those of a service that holds source
/// repositories as well as building them, for a repository under the address that connection's own
/// repositories sit under. The CI services that only build keep a run's history after its branch is
/// deleted and have no call that says it is gone, so an AppVeyor or Travis build of a GitHub
/// repository is asked of a GitHub connection.
/// <para>
/// Matched on the address each provider says its connection's repositories are under, which is
/// where its credential is already sent: a repository on github.com is asked of api.github.com, one
/// on an enterprise server of that server, and one in an Azure DevOps organization of a connection
/// to that organization. A repository no connection's address covers is never asked about, however
/// well known its host.
/// </para>
/// </summary>
static class BranchHosts
{
    /// <summary>
    /// Whether <paramref name="connection"/> can be asked about branches of the repository at
    /// <paramref name="repository"/>: its service holds repositories, and the repository is under
    /// its <see cref="IProvider.RepositoryRoot"/>.
    /// </summary>
    public static bool Answers(Connection connection, string repository) =>
        ProviderDescriptors.Find(connection.ProviderId) is { HostsRepositories: true } &&
        Providers.Get(connection.ProviderId).RepositoryRoot(connection) is { } root &&
        Uri.TryCreate(repository, UriKind.Absolute, out var url) &&
        Under(url, root);

    /// <summary>
    /// Whether <paramref name="url"/> is on <paramref name="root"/>'s host and under its path, a
    /// segment at a time, so a root of /gitlab/ does not take in /gitlabs/.
    /// </summary>
    public static bool Under(Uri url, Uri root)
    {
        var path = root.AbsolutePath;
        if (!path.EndsWith('/'))
        {
            path += '/';
        }

        return string.Equals(url.Host, root.Host, StringComparison.OrdinalIgnoreCase) &&
               url.AbsolutePath.StartsWith(path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether any of <paramref name="connections"/> can be asked about a build's branch, for
    /// <see cref="BuildSelection"/>: a failed branch that can be asked keeps its row until the answer
    /// comes, and one that cannot folds once its default branch has built since.
    /// </summary>
    public static Func<Build, bool> Askable(IEnumerable<Connection> connections)
    {
        var hosts = connections
            .Where(_ => ProviderDescriptors.Find(_.ProviderId) is { HostsRepositories: true })
            .ToList();
        if (hosts.Count == 0)
        {
            return _ => false;
        }

        return _ => _.RepoUrl is { } repository &&
                    hosts.Any(_ => Answers(_, repository));
    }
}
