/// <summary>
/// The MCP tools against an in-process tray: the real handler over the canonical state, with
/// no socket in between.
/// </summary>
public class MonitorToolsTests
{
    static (MonitorTools Tools, List<string> Opened) Create()
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        var poller = new Poller(host, new MemorySecretStore(), new(), new FakeHttpHandler());
        var opened = new List<string>();
        var handler = new MessageHandler(host, poller, opened.Add, _ => { }, () => Fixtures.Now);
        return (new(new InProcessClient(handler)), opened);
    }

    [Test]
    public async Task ListBuildsWithAndWithoutFilter()
    {
        var (tools, _) = Create();
        var all = await tools.ListBuilds(null, Cancel.None);
        var filtered = await tools.ListBuilds("verify", Cancel.None);
        await Assert.That(all.Count).IsEqualTo(6);
        await Verify(filtered);
    }

    [Test]
    public async Task ListFailing()
    {
        var (tools, _) = Create();
        var failing = await tools.ListFailing(Cancel.None);
        await Assert.That(failing.Select(_ => _.Key)).IsEquivalentTo(["gh/Verify/test.yml/feature/inline"]);
    }

    [Test]
    public async Task GetBuildAndMissing()
    {
        var (tools, _) = Create();
        var build = await tools.GetBuild("jenkins/build-all/main", Cancel.None);
        await Assert.That(build.Timing).IsEqualTo("04:00 left");
        var exception = await Assert.That(async () => await tools.GetBuild("nope", Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("No build with key nope");
    }

    [Test]
    public async Task SummaryConnectionsAndOpen()
    {
        var (tools, opened) = Create();
        var summary = await tools.Summary(Cancel.None);
        var connections = await tools.ListConnections(Cancel.None);
        var url = await tools.OpenBuild("gh/Verify/test.yml/feature/inline", "pr", Cancel.None);
        await Assert.That(opened).IsEquivalentTo([url]);
        await Verify(new { summary, connections, url });
    }

    [Test]
    public async Task RefreshMessages()
    {
        var (tools, _) = Create();
        await Assert.That(await tools.Refresh(null, Cancel.None)).IsEqualTo("Refreshing every connection");
        await Assert.That(await tools.Refresh("gh", Cancel.None)).IsEqualTo("Refreshing gh");
    }

    [Test]
    public async Task RetryARunningBuildIsRefused()
    {
        var (tools, _) = Create();
        var exception = await Assert.That(async () => await tools.RetryBuild("gh/DiffEngine/test.yml/main", Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("That build cannot be retried");
    }

    sealed class InProcessClient(MessageHandler handler) : IProtocolClient
    {
        public Task<Response> Send(Message message, Cancel cancel) =>
            handler.Handle(message);
    }
}
