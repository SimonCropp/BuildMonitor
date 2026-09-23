/// <summary>
/// Which connections can say what became of a failed branch: those of a service that holds source
/// repositories as well as building them, for a repository on that service's own host. The CI
/// services that only build keep a run's history after its branch is deleted and have no call that
/// says it is gone, so an AppVeyor or Travis build of a GitHub repository is asked of a GitHub
/// connection.
/// <para>
/// Matched on the connection's own address, which is the only place its credential is ever sent:
/// a repository on github.com is asked of api.github.com, and one on an enterprise server of that
/// server. A repository on a host no connection points at is never asked about, however well known
/// the host.
/// </para>
/// </summary>
static class BranchHosts
{
    /// <summary>
    /// Whether <paramref name="connection"/> can be asked about branches of the repository at
    /// <paramref name="repository"/>: its service holds repositories, and its address is the
    /// repository's host or that host's api subdomain, as api.github.com is github.com's. Where the
    /// two share a host, the repository must also sit under the connection's path, as it does on a
    /// GitLab served from a subdirectory.
    /// </summary>
    public static bool Answers(Connection connection, string repository)
    {
        if (ProviderDescriptors.Find(connection.ProviderId) is not { HostsRepositories: true } descriptor ||
            (connection.Server ?? descriptor.DefaultServer) is not { } server ||
            !Uri.TryCreate(ServerAddress.Normalize(server), UriKind.Absolute, out var api) ||
            !Uri.TryCreate(repository, UriKind.Absolute, out var url))
        {
            return false;
        }

        if (string.Equals(api.Host, $"api.{url.Host}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(api.Host, url.Host, StringComparison.OrdinalIgnoreCase) &&
               url.AbsolutePath.StartsWith($"{api.AbsolutePath.TrimEnd('/')}/", StringComparison.OrdinalIgnoreCase);
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
