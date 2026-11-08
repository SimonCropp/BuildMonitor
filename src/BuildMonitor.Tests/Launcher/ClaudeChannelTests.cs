using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// The channel as Claude Code sees it, over a real server and client: the capability that makes it
/// listen, the instructions that say what the events mean, and an event as it arrives. Claude Code
/// drops an event it cannot read without a word, so the shape on the wire is what is held here.
/// </summary>
public class ClaudeChannelTests
{
    [Test]
    public async Task AnEventArrivesAsClaudeCodeReadsIt()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var options = new McpServerOptions
        {
            ServerInstructions = McpInstructions.Text
        };
        ClaudeChannel.Declare(options);
        using var stop = new CancelSource();
        // Named in full: BuildMonitor has an McpServer of its own, the record of a running server shim.
        await using var server = ModelContextProtocol.Server.McpServer.Create(new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()), options);
        var running = server.RunAsync(stop.Token);
        var received = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientOptions = new McpClientOptions
        {
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new(
                        ClaudeChannel.Method,
                        (_, cancel) =>
                        {
                            received.TrySetResult(_.Params);
                            return ValueTask.CompletedTask;
                        })
                ]
            }
        };
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), clientOptions))
        {
            await Assert.That(client.ServerCapabilities.Experimental!.ContainsKey("claude/channel")).IsTrue();
            await Assert.That(client.ServerInstructions).EndsWith(McpInstructions.Channel);

            var meta = new Dictionary<string, string>
            {
                ["event"] = ChannelText.BuildFailed,
                ["key"] = "gh/Verify/test.yml/main"
            };
            await ClaudeChannel.Send(server, new("test.yml 77 failed.", meta), Cancel.None);
            using var timeout = new CancelSource(TimeSpan.FromSeconds(10));
            var parameters = await received.Task.WaitAsync(timeout.Token);
            // Content and meta are what Claude Code reads. The SDK may add _meta beside them, the
            // protocol's own member, with the trace context of whatever activity is running.
            await Assert.That(parameters!["content"]!.GetValue<string>()).IsEqualTo("test.yml 77 failed.");
            await Assert.That(parameters["meta"]!.ToJsonString()).IsEqualTo("""{"event":"build_failed","key":"gh/Verify/test.yml/main"}""");
        }

        await stop.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// What an assistant sees of watch_build: the choices wait_for_build takes, and nothing the
    /// SDK binds itself.
    /// </summary>
    [Test]
    public async Task WatchBuildTakesTheChoicesWaitForBuildDoes()
    {
        var method = typeof(ChannelTools).GetMethod(nameof(ChannelTools.WatchBuild))!;
        var tool = McpServerTool.Create(
            method,
            _ => throw new InvalidOperationException(),
            new()
            {
                SerializerOptions = McpHost.ToolSerializerOptions
            });
        await Assert.That(tool.ProtocolTool.Name).IsEqualTo("watch_build");
        var names = tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(_ => _.Name);
        await Assert.That(string.Join(", ", names)).IsEqualTo("key, nextRun, wholePush, stopOnFailure");
    }
}
