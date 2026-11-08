using System.IO.Pipelines;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// The tools' and prompts' own failures reach the client as they read. The SDK puts a generic line
/// in place of the message of any exception but an McpException, and every tool's failure, and the
/// triage prompt's, had been arriving as "An error occurred invoking" it, whatever the tray had
/// said. The server tests run a real server and client over pipes, so they hold the SDK to it: a
/// filter that ran inside the SDK's own catch, rather than outside it, would pass the unit tests and
/// still change nothing.
/// </summary>
public class ErrorRelayTests
{
    const string refusal = "No build with key nope";

    static Func<string> Refuse { get; } = () => throw new InvalidOperationException(refusal);

    /// <summary>
    /// One tool and one prompt that fail as a refusal from the tray does, with the relay added as
    /// the host adds it, or not.
    /// </summary>
    static async Task Serve(bool relay, Func<McpClient, Task> use)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var options = new McpServerOptions
        {
            ToolCollection = [McpServerTool.Create(Refuse, new() { Name = "refuse" })],
            PromptCollection = [McpServerPrompt.Create(Refuse, new() { Name = "refuse" })]
        };
        if (relay)
        {
            ErrorRelay.AddTo(options.Filters.Request);
        }

        using var stop = new CancelSource();
        // Named in full: BuildMonitor has an McpServer of its own, the record of a running server shim.
        await using var server = ModelContextProtocol.Server.McpServer.Create(new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()), options);
        var running = server.RunAsync(stop.Token);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream())))
        {
            await use(client);
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

    static string Text(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(_ => _.Text));

    [Test]
    public Task AToolsFailureReadsAsItSaid() =>
        Serve(
            true,
            async _ =>
            {
                var result = await _.CallToolAsync("refuse");
                await Assert.That(result.IsError).IsTrue();
                await Assert.That(Text(result)).Contains(refusal);
            });

    [Test]
    public Task APromptsFailureReadsAsItSaid() =>
        Serve(
            true,
            async _ =>
            {
                var exception = await Assert.That(async () => await _.GetPromptAsync("refuse")).Throws<McpException>();
                await Assert.That(exception!.Message).Contains(refusal);
            });

    /// <summary>
    /// What the relay is for: without it the SDK sends its own line in place of the refusal. Should
    /// a later SDK pass messages on, this fails, and the relay can go.
    /// </summary>
    [Test]
    public Task WithoutTheRelayTheSdkHidesTheFailure() =>
        Serve(
            false,
            async _ =>
            {
                var result = await _.CallToolAsync("refuse");
                await Assert.That(Text(result)).DoesNotContain(refusal);
                var exception = await Assert.That(async () => await _.GetPromptAsync("refuse")).Throws<McpException>();
                await Assert.That(exception!.Message).DoesNotContain(refusal);
            });

    [Test]
    public async Task ATrayThatIsNotRunningIsPassedOnAsItReads()
    {
        var relay = ErrorRelay.Relay<CallToolRequestParams, CallToolResult>((_, _) => throw new TrayUnreachableException(1234, new IOException()));
        var exception = await Assert.That(async () => await relay(null!, Cancel.None)).Throws<McpException>();
        await Assert.That(exception!.Message).IsEqualTo("No BuildMonitor tray is listening on port 1234");
    }

    /// <summary>
    /// Anything else is a fault, whose message was written for a log, and keeps the SDK's line.
    /// </summary>
    [Test]
    public async Task AFaultIsLeftToTheSdk() =>
        await Assert.That(async () => await ErrorRelay.Relay<CallToolRequestParams, CallToolResult>((_, _) => throw new NullReferenceException())(null!, Cancel.None))
            .ThrowsExactly<NullReferenceException>();

    [Test]
    public async Task CancellingIsNotAFailure() =>
        await Assert.That(async () => await ErrorRelay.Relay<CallToolRequestParams, CallToolResult>((_, _) => throw new OperationCanceledException())(null!, Cancel.None))
            .ThrowsExactly<OperationCanceledException>();
}
