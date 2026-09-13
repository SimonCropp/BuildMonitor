/// <summary>
/// The loopback port the tray binds. Whoever binds it is the running instance; the launcher and
/// the MCP server talk to it.
/// </summary>
static class Port
{
    public const int Default = 3796;
    const string variable = "BuildMonitor_Port";

    /// <summary>
    /// The environment wins over settings, so a test or a second profile can run beside a real
    /// tray without editing its settings.json.
    /// </summary>
    public static int Resolve(Settings? settings = null)
    {
        var variable = Environment.GetEnvironmentVariable(Port.variable);
        if (!string.IsNullOrWhiteSpace(variable) &&
            int.TryParse(variable, out var fromEnvironment) &&
            fromEnvironment > 0)
        {
            return fromEnvironment;
        }

        if (settings is { Port: > 0 })
        {
            return settings.Port;
        }

        return Default;
    }
}
