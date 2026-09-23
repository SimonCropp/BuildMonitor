/// <summary>
/// The branch a pull request's run is filed under: the one it built, never the one it would merge
/// into. AppVeyor and Travis put the target in a PR build's branch, so every pull request's run read
/// as a run of main, took main's key and, being the newest, main's row, with a PR button beside a
/// branch it had nothing to do with.
/// </summary>
static class PullRequestBranches
{
    /// <summary>
    /// The head branch, behind its owner where it lives in a fork: someone:main, as GitHub writes
    /// it. A fork's branch is often named after one upstream, most often main, and without the owner
    /// a fork's pull request took upstream main's key.
    /// </summary>
    public static string Head(string branch, string? forkOwner)
    {
        if (forkOwner is null)
        {
            return branch;
        }

        return $"{forkOwner}:{branch}";
    }

    /// <summary>
    /// For a service that names no head branch, the pull request's own ref: pull/12. Each pull
    /// request still gets a key of its own rather than every one of them sharing the target's, or
    /// an empty one.
    /// </summary>
    public static string Unnamed(string number) =>
        $"pull/{number}";

    /// <summary>
    /// The owner of <paramref name="headRepository"/> where it is another repository than
    /// <paramref name="repository"/>, both as owner/name, ignoring case as the hosts do. Null for a
    /// pull request from a branch of the repository itself, and wherever either is not a pair to
    /// compare, since a guessed owner would split one branch's runs across two keys.
    /// </summary>
    public static string? ForkOwner(string? headRepository, string repository)
    {
        if (headRepository is null ||
            string.Equals(headRepository, repository, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slash = headRepository.IndexOf('/');
        if (slash <= 0 ||
            !repository.Contains('/'))
        {
            return null;
        }

        return headRepository[..slash];
    }
}
