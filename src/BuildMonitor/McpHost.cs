using ModelContextProtocol.Protocol;

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
    public static JsonSerializerOptions ToolSerializerOptions { get; } =
        new(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

    /// <summary>
    /// Who the server says it is when a client connects. With no icons a client draws a letter
    /// tile from the name, so the idle tray icon goes with it, as data URIs: an https URL would
    /// need the client online, and would leave every installed version pointing at whatever the
    /// URL serves.
    /// </summary>
    public static Implementation ServerInfo { get; } = new()
    {
        Name = "BuildMonitor",
        Title = "BuildMonitor",
        Version = VersionReader.VersionString,
        Icons = [.. ServerIcons()]
    };

    /// <summary>
    /// A size Images is missing is left out rather than thrown on, as a head started from a
    /// checkout that never ran IconBuilder starts with no icon.
    /// </summary>
    static IEnumerable<Icon> ServerIcons()
    {
        foreach (var size in (int[]) [48, 256])
        {
            var png = Images.TrayPng(TrayIconKind.Idle, size);
            if (png is null)
            {
                continue;
            }

            yield return new()
            {
                Source = $"data:image/png;base64,{Convert.ToBase64String(png)}",
                MimeType = "image/png",
                Sizes = [$"{size}x{size}"]
            };
        }
    }

    /// <param name="channel">Whether to be a Claude Code channel: see <see cref="ClaudeChannel"/>.</param>
    public static async Task<int> Run(int port, bool channel, Cancel cancel)
    {
        var started = await HeadLauncher.StartOrShow(port, show: false, cancel);
        if (started != 0)
        {
            return started;
        }

        using var collector = new IdleCollector(IdleCollector.PoolTrimAge);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(_ => _.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IProtocolClient>(new ProtocolClient(port));
        builder.Services.AddSingleton<MonitorTools>();
        var server = builder.Services
            .AddMcpServer(_ =>
            {
                _.ServerInfo = ServerInfo;
                _.ServerInstructions = McpInstructions.Text;
                ErrorRelay.AddTo(_.Filters.Request);
                if (channel)
                {
                    ClaudeChannel.Declare(_);
                }
            });
        server
            .WithStdioServerTransport()
            .WithRequestFilters(_ => _.AddCallToolFilter(collector.After))
            .WithTools<BuildTools>(ToolSerializerOptions)
            .WithPrompts<BuildPrompts>();
        if (channel)
        {
            ClaudeChannel.Add(builder.Services, server);
        }

        await builder.Build().RunAsync(cancel);
        return 0;
    }
}
