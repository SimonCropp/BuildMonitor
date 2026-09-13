static class CommandLine
{
    public const string Usage =
        """
        buildmonitor            start the tray, or show it if it is already running
        buildmonitor show       show the window
        buildmonitor hide       hide the window
        buildmonitor quit       exit the tray
        buildmonitor refresh    poll every connection now
        buildmonitor status     print the connections and builds
        buildmonitor mcp        run the MCP server over stdio for an AI assistant
        buildmonitor --version
        buildmonitor --help
        """;

    public static Command Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new(CommandKindLauncher.Start);
        }

        var verb = args[0].Trim().TrimStart('-').ToLowerInvariant();
        var argument = args.Length > 1 ? args[1] : null;
        return verb switch
        {
            "start" => new(CommandKindLauncher.Start),
            "show" or "open" => new(CommandKindLauncher.Show),
            "hide" => new(CommandKindLauncher.Hide),
            "quit" or "exit" or "stop" => new(CommandKindLauncher.Quit),
            "refresh" => new(CommandKindLauncher.Refresh, argument),
            "status" or "list" => new(CommandKindLauncher.Status),
            "mcp" => new(CommandKindLauncher.Mcp),
            "version" or "v" => new(CommandKindLauncher.Version),
            "help" or "h" or "?" => new(CommandKindLauncher.Help),
            _ => new(CommandKindLauncher.Unknown, args[0])
        };
    }
}
