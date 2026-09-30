/// <summary>
/// Windows 11 remembers whether a tray icon sits on the taskbar or behind the chevron per
/// executable path, and puts an icon from a path it has not seen behind the chevron. The installed
/// head runs from a versioned directory in the tool store, so every update was a path Windows had
/// not seen, and an icon the user had put on the taskbar went back behind the chevron after each
/// one. Nothing an app can write moves its own icon: Explorer rewrites IsPromoted under
/// HKCU\Control Panel\NotifyIconSettings from what it holds, and the icon stays where it was.
/// <para>
/// So the launcher starts the head from a copy at one path that every version shares, refreshed
/// from the installed version before each start, and the choice the user makes for that path holds
/// through every update.
/// </para>
/// </summary>
static class HeadCopy
{
    const string logs = "logs";

    /// <summary>
    /// The head to start: the copy in <paramref name="target"/>, or <paramref name="head"/> itself.
    /// A head outside the tool store is a build run from its bin directory, whose copy would land on
    /// the installed one's. A copy that cannot be refreshed is one a head still running from it
    /// holds open, and starting the installed head instead costs only the icon's placement, where
    /// starting a copy half refreshed would mix two versions.
    /// </summary>
    public static string Prepare(string head, string target)
    {
        if (ToolStorePath.Parse(head) is null)
        {
            return head;
        }

        var source = Path.GetDirectoryName(head)!;
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                // Written by a head that ran from the store before there was a copy.
                if (relative.StartsWith(logs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destination = Path.Combine(target, relative);
                if (Unchanged(file, destination))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
            }
        }
        catch (Exception exception)
            when (exception is
                      IOException or
                      UnauthorizedAccessException)
        {
            return head;
        }

        return Path.Combine(target, Path.GetFileName(head));
    }

    /// <summary>
    /// Skipping what is already there lets a second launcher, started while the first one's head is
    /// running from the copy, get past files that head holds open.
    /// </summary>
    static bool Unchanged(string source, string destination)
    {
        var existing = new FileInfo(destination);
        if (!existing.Exists)
        {
            return false;
        }

        var file = new FileInfo(source);
        return existing.Length == file.Length &&
               existing.LastWriteTimeUtc == file.LastWriteTimeUtc;
    }
}
