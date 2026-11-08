using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

/// <summary>
/// The server as a Claude Code channel: https://code.claude.com/docs/en/channels-reference. A
/// channel pushes events into a session, which the session reads as a <c>&lt;channel&gt;</c> tag and
/// acts on even while idle: the one way to tell an assistant that main went red without it asking.
/// <para>
/// Opt in, with <c>buildmonitor mcp --channel</c>, rather than for every server. The server cannot
/// tell whether its client loaded it as a channel, and Claude Code drops the events of one it did
/// not without a word, so every server would poll the tray for every client, Cursor's and Claude
/// Desktop's chats among them, for events nothing reads. Loading a server as a channel takes a
/// flag of its own while channels are a research preview, so whoever sets that up can add this.
/// </para>
/// </summary>
static class ClaudeChannel
{
    public const string Method = "notifications/claude/channel";

    /// <summary>
    /// The capability that makes Claude Code listen for <see cref="Method"/>, and the instructions
    /// that say what the events mean.
    /// </summary>
    public static void Declare(McpServerOptions options)
    {
        options.Capabilities ??= new();
        options.Capabilities.Experimental ??= new Dictionary<string, object>();
        options.Capabilities.Experimental["claude/channel"] = new JsonObject();
        options.ServerInstructions = $"{options.ServerInstructions}\n\n{McpInstructions.Channel}";
    }

    /// <summary>
    /// The feed, its failure watch for the life of the server, and watch_build. The server is
    /// resolved when an event is sent, not when the feed is made, since the host makes the feed's
    /// first user, the failure watch, before it has started the server.
    /// </summary>
    public static void Add(IServiceCollection services, IMcpServerBuilder server)
    {
        services.AddSingleton(_ => new ChannelFeed(
            _.GetRequiredService<MonitorTools>(),
            (channelEvent, cancel) => Send(_.GetRequiredService<ModelContextProtocol.Server.McpServer>(), channelEvent, cancel)));
        services.AddHostedService<FailureWatchService>();
        server.WithTools<ChannelTools>();
    }

    /// <summary>
    /// Through the options the tools' results go through, which write the event as the camel cased
    /// content and meta Claude Code reads.
    /// </summary>
    public static Task Send(ModelContextProtocol.Server.McpServer server, ChannelEvent channelEvent, Cancel cancel) =>
        server.SendNotificationAsync(Method, channelEvent, McpHost.ToolSerializerOptions, cancel);
}
