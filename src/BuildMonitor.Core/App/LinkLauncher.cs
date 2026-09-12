/// <summary>
/// Opens a URL in the user's browser, on whichever desktop this is.
/// </summary>
static class LinkLauncher
{
    /// <summary>
    /// Replaceable so tests never open a browser.
    /// </summary>
    public static Action<string> OpenUrl { get; set; } = Launch;

    static void Launch(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }

            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            using var unix = Process.Start(new ProcessStartInfo(opener, [url])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not open {Url}", url);
        }
    }
}
