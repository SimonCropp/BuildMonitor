/// <summary>
/// The mark drawn before a row's repository name, which says where the source is rather than
/// where it built. Read from the repository URL's own host, for the same reason the tooltip beside
/// it is: an enterprise server runs GitHub under a name of its owner's choosing, and the provider
/// a build came from need not be the one hosting the code at all. An AppVeyor or Travis row is the
/// ordinary case of that.
/// <para>
/// A host nothing here has a mark for gets none, and the name is drawn as text alone. Guessing
/// would put the wrong logo on a row, which is the confusion the two marks exist to end.
/// </para>
/// </summary>
static class RepoHosts
{
    const string github = "host-github";
    const string gitlab = "host-gitlab";
    const string bitbucket = "host-bitbucket";
    const string azure = "host-azure-devops";

    /// <summary>
    /// Every mark <see cref="MarkOf"/> can return. A head hands its pictures over once, before the
    /// first frame, rather than as a row asks for one, so it needs the whole set up front.
    /// </summary>
    public static readonly string[] All = [github, gitlab, bitbucket, azure];

    /// <summary>
    /// The mark for <paramref name="url"/>, or empty for a host with none. Matched against the
    /// labels of the host rather than the whole of it, so a self hosted server keeps the mark of
    /// what it runs: github.example.com is GitHub. Only the host, since a path can say anything:
    /// example.com/github is somebody's repository named github, on nobody knows what.
    /// </summary>
    public static string MarkOf(string? url)
    {
        if (Known(url) is { } known)
        {
            return known.Mark;
        }

        return "";
    }

    /// <summary>
    /// What to call the service holding <paramref name="url"/>, for a hover that says where a
    /// click goes: the name of the one whose mark it carries, or the host itself where there is
    /// no mark for it. An enterprise server is named for what it runs rather than for its own
    /// address, since that is the thing being opened; a host nobody here knows is named by its
    /// address, which is all that is known about it.
    /// </summary>
    public static string NameOf(string url)
    {
        if (Known(url) is { } known)
        {
            return known.Name;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return parsed.Host;
        }

        return url;
    }

    static (string Mark, string Name)? Known(string? url)
    {
        if (url is null ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        foreach (var label in parsed.Host.Split('.'))
        {
            if (Is(label, "github"))
            {
                return (github, "GitHub");
            }

            if (Is(label, "gitlab"))
            {
                return (gitlab, "GitLab");
            }

            if (Is(label, "bitbucket"))
            {
                return (bitbucket, "Bitbucket");
            }

            // dev.azure.com, and the {account}.visualstudio.com it grew out of, which still answers.
            if (Is(label, "azure") ||
                Is(label, "visualstudio"))
            {
                return (azure, "Azure DevOps");
            }
        }

        return null;
    }

    static bool Is(string label, string host) =>
        string.Equals(label, host, StringComparison.OrdinalIgnoreCase);
}
