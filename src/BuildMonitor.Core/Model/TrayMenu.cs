/// <summary>
/// The ids of the fixed tray menu items, and how the per build ones are composed.
/// </summary>
static class TrayMenu
{
    public const string Open = "open";
    public const string Refresh = "refresh";
    public const string Options = "options";
    public const string Filters = "filters";
    public const string Logs = "logs";
    public const string Issue = "issue";
    public const string Update = "update";
    public const string Exit = "exit";
    public const string Overflow = "overflow";

    /// <summary>
    /// How many builds the dynamic section shows before it stops, across every connection. A tray
    /// menu taller than the screen is unusable, and a monitor with more than twenty red builds
    /// has a bigger problem than the menu.
    /// </summary>
    public const int MaxBuilds = 20;

    public static string BuildItem(Build build, string action) =>
        $"build:{action}:{build.Key}";

    public static bool TryParseBuildItem(string id, [NotNullWhen(true)] out string? action, [NotNullWhen(true)] out string? key)
    {
        action = null;
        key = null;
        if (!id.StartsWith("build:", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = id.AsSpan(6);
        var separator = rest.IndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        action = rest[..separator].ToString();
        key = rest[(separator + 1)..].ToString();
        return true;
    }

    public const string OpenAction = "open";
    public const string RetryAction = "retry";
    public const string CancelAction = "cancel";
}
