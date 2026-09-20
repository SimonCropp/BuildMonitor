/// <summary>
/// The MCP servers running from the installed shim, and whether an update stops them. What the
/// update page warns about before it takes the tray away.
/// </summary>
/// <param name="StoppedByUpdate">Windows cannot replace a file that is open, so the update stops
/// these before it runs and an AI client mid-conversation loses its server. Every other platform
/// can, so they are left running and keep the version they started with until their client
/// connects again.</param>
record McpServers(ImmutableArray<McpServer> Running, bool StoppedByUpdate)
{
    public static McpServers None = new([], false);

    /// <summary>
    /// Matched on the shim's path, which is one of the two things the update script matches on, so
    /// every server the script stops is warned about here. The other is the store directory, which
    /// no server of this shim runs from. A build run from anywhere else is left alone.
    /// <para>
    /// Nothing here is allowed to throw. A process can exit between being listed and being read,
    /// and one belonging to another user cannot be read at all; either way it is not a server this
    /// update is going to stop, so it is left out rather than reported.
    /// </para>
    /// </summary>
    public static McpServers Find()
    {
        var shim = ShimPath.Resolve();
        var running = ImmutableArray.CreateBuilder<McpServer>();
        try
        {
            foreach (var process in Process.GetProcessesByName(ShimPath.Command))
            {
                using (process)
                {
                    if (Describe(process, shim) is { } server)
                    {
                        running.Add(server);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not list the running MCP servers");
        }

        running.Sort((left, right) => left.Started.CompareTo(right.Started));
        return new(running.ToImmutable(), OperatingSystem.IsWindows());
    }

    static McpServer? Describe(Process process, string shim)
    {
        try
        {
            if (process.Id == Environment.ProcessId ||
                process.MainModule?.FileName is not { } path ||
                !string.Equals(path, shim, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new(process.Id, process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
