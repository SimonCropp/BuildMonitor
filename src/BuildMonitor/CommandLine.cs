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
          --channel             and push build events into Claude Code, which loads it as a channel
        buildmonitor --version
        buildmonitor --help
        """;

    /// <summary>
    /// The option that makes the MCP server a Claude Code channel.
    /// </summary>
    public const string Channel = "--channel";

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
            "mcp" => Mcp(argument),
            "version" or "v" => new(CommandKindLauncher.Version),
            "help" or "h" or "?" => new(CommandKindLauncher.Help),
            _ => new(CommandKindLauncher.Unknown, args[0])
        };
    }

    /// <summary>
    /// Anything after mcp but the channel option is refused, rather than run as a server without
    /// it: a mistyped option left the user waiting on events nothing was ever going to send.
    /// </summary>
    static Command Mcp(string? argument)
    {
        if (argument is null)
        {
            return new(CommandKindLauncher.Mcp);
        }

        if (string.Equals(argument, Channel, StringComparison.OrdinalIgnoreCase))
        {
            return new(CommandKindLauncher.Mcp, Channel);
        }

        return new(CommandKindLauncher.Unknown, $"mcp {argument}");
    }
}
