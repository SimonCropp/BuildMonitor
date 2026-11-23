/// <summary>
/// Cloning a GitHub repository that has no checkout under the code directory, through the link
/// GitHub Desktop registers, the one github.com's own "Open with GitHub Desktop" uses. Desktop
/// answers it with its clone dialog, the address filled in and the folder left for the user to
/// pick, so nothing is cloned until a second click there; for a repository it already has, it
/// opens that one instead.
/// <para>
/// <see cref="CloneLink(SessionState, Build)"/> is the one rule: the chip, the menu item and the
/// click all go through it, so a row cannot offer a clone that then opens nothing, and a checkout
/// found between the frame and the click opens nothing rather than a second clone of it.
/// </para>
/// </summary>
static class GitHubDesktop
{
    /// <summary>
    /// What GitHub Desktop registers on Windows and macOS. There is no Linux build of it, so there
    /// the scheme is never found and no row offers a clone.
    /// </summary>
    public const string Scheme = "x-github-client";

    /// <summary>
    /// Whether this machine has an app for <see cref="Scheme"/> links. Replaceable so a test never
    /// asks the desktop.
    /// </summary>
    public static Func<bool> Installed { get; set; } = () => LinkHandlers.Exists(Scheme);

    public static string? CloneLink(SessionState state, Build build) =>
        CloneLink(state.GitHubDesktop, state.ScannedCodeDirectory, state.LocalRepos, build);

    /// <summary>
    /// The link that hands <paramref name="build"/>'s repository to GitHub Desktop, or null where
    /// no clone is offered: Desktop was not found, no code directory has been scanned, the
    /// repository is checked out there already, or it is not on GitHub.
    /// <para>
    /// "Not checked out" is only known of a directory that was looked in. With no code directory
    /// set, or before the first scan lands, every GitHub row would offer a clone, the repositories
    /// checked out included.
    /// </para>
    /// </summary>
    /// <param name="installed">Whether Desktop's link handler was found.</param>
    /// <param name="scanned">The code directory <paramref name="localRepos"/> was scanned from, or
    /// null.</param>
    public static string? CloneLink(bool installed, string? scanned, ImmutableDictionary<string, string> localRepos, Build build)
    {
        if (!installed ||
            string.IsNullOrEmpty(scanned) ||
            !RepoHosts.IsGitHub(build.RepoUrl) ||
            LocalRepos.Find(localRepos, build) is not null)
        {
            return null;
        }

        // Desktop takes the repository's page as it is and adds the .git itself.
        return $"{Scheme}://openRepo/{build.RepoUrl!.TrimEnd('/')}";
    }
}
