/// <summary>
/// Reads a checkout's origin out of its git config, as the path a CI service names it by.
/// <para>
/// The config is parsed rather than shelled out to, because <c>git config</c> costs a process per
/// repository and a code directory can hold hundreds. Only the origin's URL is read and nothing is
/// written, so a checkout mid-rebase is unaffected.
/// </para>
/// </summary>
static class GitRemote
{
    /// <summary>
    /// The origin of the checkout at <paramref name="directory"/>, reduced to "owner/repo", or null
    /// where there is no config, no origin, or nothing that parses as a URL.
    /// </summary>
    public static string? Of(string directory)
    {
        var config = ConfigPath(directory);
        if (config is null)
        {
            return null;
        }

        try
        {
            return PathOf(OriginUrl(File.ReadLines(config)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "Could not read the git config of {Directory}", directory);
            return null;
        }
    }

    /// <summary>
    /// The config of a checkout, whose .git is a directory in the ordinary case and a file holding
    /// "gitdir: ..." in a worktree or a submodule.
    /// </summary>
    static string? ConfigPath(string directory)
    {
        var git = Path.Combine(directory, ".git");
        if (Directory.Exists(git))
        {
            return Path.Combine(git, "config");
        }

        if (!File.Exists(git))
        {
            return null;
        }

        try
        {
            var pointer = File.ReadAllText(git).Trim();
            if (!pointer.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return null;
            }

            var target = pointer["gitdir:".Length..].Trim();
            if (!Path.IsPathRooted(target))
            {
                target = Path.GetFullPath(Path.Combine(directory, target));
            }

            var config = Path.Combine(target, "config");
            if (File.Exists(config))
            {
                return config;
            }

            // A worktree's gitdir points at .git/worktrees/<name> in the main checkout, which holds
            // no config of its own: the remotes are the main one's, two directories up.
            var shared = Path.GetFullPath(Path.Combine(target, "..", "..", "config"));
            if (File.Exists(shared))
            {
                return shared;
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "Could not follow the gitdir of {Directory}", directory);
            return null;
        }
    }

    /// <summary>
    /// The url of [remote "origin"]. Read by hand rather than with an ini parser because the only
    /// shape that matters is a url line inside that one section.
    /// </summary>
    static string? OriginUrl(IEnumerable<string> lines)
    {
        var inOrigin = false;
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.StartsWith('['))
            {
                inOrigin = text
                    .Replace(" ", "")
                    .Equals("[remote\"origin\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inOrigin)
            {
                continue;
            }

            var equals = text.IndexOf('=');
            if (equals <= 0 ||
                !text[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = text[(equals + 1)..].Trim();
            if (url.Length > 0)
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>
    /// The path part of a clone URL, which is what GitHub, Travis and Bitbucket report as a
    /// repository name and GitLab as a namespaced path. Handles the shapes git writes: scp-like
    /// "git@host:owner/repo.git", "https://host/owner/repo.git" and "ssh://host/owner/repo.git".
    /// </summary>
    public static string? PathOf(string? url)
    {
        if (url is null)
        {
            return null;
        }

        var text = url.Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var afterScheme = text[(scheme + 3)..];
            var slash = afterScheme.IndexOf('/');
            text = slash < 0 ? "" : afterScheme[(slash + 1)..];
        }
        else
        {
            // Everything before the colon of an scp-like URL is user@host, and it carries no port.
            var colon = text.IndexOf(':');
            if (colon > 0)
            {
                text = text[(colon + 1)..];
            }
        }

        text = text.Trim('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        if (text.Length == 0)
        {
            return null;
        }

        return text;
    }
}
