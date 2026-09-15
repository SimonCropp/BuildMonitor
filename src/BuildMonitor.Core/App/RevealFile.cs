/// <summary>
/// Opens a directory in the desktop's file manager.
/// </summary>
static class RevealFile
{
    public static void OpenDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsWindows())
            {
                using var explorer = Process.Start(
                    new ProcessStartInfo(
                        "explorer.exe", [directory])
                    {
                        UseShellExecute = true
                    });
                return;
            }

            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            using var process = Process.Start(
                new ProcessStartInfo(opener, [directory])
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not open {Directory}", directory);
        }
    }
}
