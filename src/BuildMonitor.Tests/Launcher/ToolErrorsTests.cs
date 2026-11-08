using ModelContextProtocol;

/// <summary>
/// The tools' own failures reach the assistant as they read. The SDK puts a generic line in place
/// of the message of any exception but an McpException, and every tool's failure had been
/// arriving as "An error occurred invoking" the tool, whatever the tray had said.
/// </summary>
public class ToolErrorsTests
{
    [Test]
    public async Task ARefusalIsPassedOnAsItReads()
    {
        var relay = ToolErrors.Relay((_, _) => throw new InvalidOperationException("No build with key nope"));
        var exception = await Assert.That(async () => await relay(null!, Cancel.None)).Throws<McpException>();
        await Assert.That(exception!.Message).IsEqualTo("No build with key nope");
    }

    [Test]
    public async Task ATrayThatIsNotRunningIsPassedOnAsItReads()
    {
        var relay = ToolErrors.Relay((_, _) => throw new TrayUnreachableException(1234, new IOException()));
        var exception = await Assert.That(async () => await relay(null!, Cancel.None)).Throws<McpException>();
        await Assert.That(exception!.Message).IsEqualTo("No BuildMonitor tray is listening on port 1234");
    }

    /// <summary>
    /// Anything else is a fault, whose message was written for a log, and keeps the SDK's line.
    /// </summary>
    [Test]
    public async Task AFaultIsLeftToTheSdk() =>
        await Assert.That(async () => await ToolErrors.Relay((_, _) => throw new NullReferenceException())(null!, Cancel.None))
            .ThrowsExactly<NullReferenceException>();

    [Test]
    public async Task CancellingIsNotAFailure() =>
        await Assert.That(async () => await ToolErrors.Relay((_, _) => throw new OperationCanceledException())(null!, Cancel.None))
            .ThrowsExactly<OperationCanceledException>();
}
