/// <summary>
/// Opens a directory in the desktop's file manager. Does not create it: a checkout that has been
/// deleted since its row was drawn should open nothing, not leave an empty folder behind.
/// </summary>
static class RevealFile
{
    public static void OpenDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                Log.Debug("{Directory} is gone, so there is nothing to open", directory);
                return;
            }

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
