/// <summary>
/// Opens a URL in the user's browser, on whichever desktop this is: see <see cref="LinkOpener"/>.
/// </summary>
static class LinkLauncher
{
    /// <summary>
    /// Replaceable so tests never open a browser.
    /// </summary>
    public static Action<string> OpenUrl { get; set; } = Launch;

    static void Launch(string url)
    {
        if (!LinkOpener.TryOpen(url, out var error))
        {
            Log.Error(error, "Could not open {Url}", url);
        }
    }
}
