/// <summary>
/// The stdio MCP server. Stdout is the protocol channel, so every log line goes to stderr. The
/// tray is started first if it is not running; the tools then talk to it over the socket.
/// </summary>
static class McpHost
{
    public static async Task<int> Run(int port, Cancel cancel)
    {
        var started = await HeadLauncher.StartOrShow(port, show: false, cancel);
        if (started != 0)
        {
            return started;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(_ => _.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IProtocolClient>(new ProtocolClient(port));
        builder.Services.AddSingleton<MonitorTools>();
        builder.Services
            .AddMcpServer(_ =>
            {
                _.ServerInfo = new()
                {
                    Name = "BuildMonitor",
                    Version = VersionReader.VersionString
                };
            })
            .WithStdioServerTransport()
            .WithTools<BuildTools>();
        await builder.Build().RunAsync(cancel);
        return 0;
    }
}
