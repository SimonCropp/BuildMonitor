/// <summary>
/// A folder chooser for a head whose library has no panel of its own, which is the raylib one:
/// neither desktop's chooser can be drawn convincingly in Dear ImGui, and every Linux desktop
/// already ships one as a program. macOS does not come here; its head puts up an NSOpenPanel
/// through bm_pick_directory, which is modal to the app rather than a window of another process.
/// <para>
/// Null rather than an exception when nothing is installed, which is ordinary on a minimal Linux
/// desktop. The path field beside the Browse button is the fallback, so a missing chooser costs
/// the user a paste rather than the option.
/// </para>
/// </summary>
static class DirectoryPicker
{
    /// <summary>
    /// A chooser is waited on for as long as the user takes to find a folder, not the thirty
    /// seconds a captured command is allowed.
    /// </summary>
    static readonly TimeSpan patience = TimeSpan.FromMinutes(10);

    public static string? Pick(string? start)
    {
        try
        {
            var picked = Ask(start);
            if (picked is null)
            {
                return null;
            }

            var directory = picked.Trim().TrimEnd('/');
            return directory.Length == 0 || !Directory.Exists(directory) ? null : directory;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not ask for a directory");
            return null;
        }
    }

    /// <summary>
    /// A cancel exits non-zero, which is how a cancel and a failure both come back as null.
    /// </summary>
    static string? Ask(string? start)
    {
        if (ProcessRunner.OnPath("zenity") is not null)
        {
            var arguments = new List<string> { "--file-selection", "--directory", "--title=Choose your code directory" };
            if (Directory.Exists(start))
            {
                // The trailing separator is what tells zenity to open inside the folder rather
                // than beside it with the folder selected.
                arguments.Add($"--filename={start}{Path.DirectorySeparatorChar}");
            }

            var (code, output) = ProcessRunner.Run("zenity", arguments, timeout: patience);
            return code == 0 ? output : null;
        }

        if (ProcessRunner.OnPath("kdialog") is null)
        {
            Log.Information("Neither zenity nor kdialog is installed, so the code directory has to be typed");
            return null;
        }

        var (kdialogCode, kdialogOutput) = ProcessRunner.Run(
            "kdialog",
            ["--getexistingdirectory", Directory.Exists(start) ? start : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)],
            timeout: patience);
        return kdialogCode == 0 ? kdialogOutput : null;
    }
}
