using System.Text.Json.Serialization;
using ModelContextProtocol;

/// <summary>
/// The stdio MCP server. Stdout is the protocol channel, so every log line goes to stderr. The
/// tray is started first if it is not running; the tools then talk to it over the socket.
/// </summary>
static class McpHost
{
    /// <summary>
    /// What the tools' arguments and results are marshalled with, and what their output schemas are
    /// generated from. The SDK's defaults leave a null member out of the JSON, while the schema it
    /// generates from the same record lists every constructor parameter without a default as
    /// required, nullable or not: a build with no pull request came back without pullRequest, and a
    /// client validating structured content against the schema rejected the whole call. So nulls
    /// are written. A member the record makes optional, by giving it a default, is not required by
    /// the schema, and opts back out of being written when null with its own JsonIgnore.
    /// </summary>
    public static JsonSerializerOptions ToolSerializerOptions { get; } = new(McpJsonUtilities.DefaultOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

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
                _.ServerInstructions = McpInstructions.Text;
            })
            .WithStdioServerTransport()
            .WithTools<BuildTools>(ToolSerializerOptions)
            .WithPrompts<BuildPrompts>();
        await builder.Build().RunAsync(cancel);
        return 0;
    }
}
