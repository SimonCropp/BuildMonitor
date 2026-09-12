/// <summary>
/// The tray's socket, end to end: a real listener on an ephemeral port, a real client, the real
/// handler over a state with builds in it.
/// </summary>
public class LocalServerTests
{
    static async Task<(LocalServer Server, ProtocolClient Client, SessionHost Host, List<string> Opened, List<WindowCommand> Windows, Poller Poller, Task Listening, CancelSource Cancel)> Start()
    {
        await Assert.That(LocalServer.TryBind(0, out var server)).IsTrue();
        var host = new SessionHost(Fixtures.WithBuilds());
        var secrets = new MemorySecretStore();
        var handler = new FakeHttpHandler()
            .Map("POST", "https://api.github.com/repos/VerifyTests/Verify/actions/runs/77/rerun-failed-jobs", "", HttpStatusCode.Created);
        var poller = new Poller(host, secrets, new(), handler);
        var opened = new List<string>();
        var windows = new List<WindowCommand>();
        var cancel = new CancelSource();
        var listening = server!.Listen(new MessageHandler(host, poller, opened.Add, windows.Add, () => Fixtures.Now).Handle, cancel.Token);
        return (server, new(server.Port), host, opened, windows, poller, listening, cancel);
    }

    static async Task Stop((LocalServer Server, ProtocolClient Client, SessionHost Host, List<string> Opened, List<WindowCommand> Windows, Poller Poller, Task Listening, CancelSource Cancel) started)
    {
        await started.Cancel.CancelAsync();
        started.Server.Dispose();
        await started.Listening;
        await started.Poller.DisposeAsync();
        started.Cancel.Dispose();
    }

    [Test]
    public async Task PingAnswersWithTheVersion()
    {
        var started = await Start();
        try
        {
            var response = await started.Client.Send(new(Verb.Ping), Cancel.None);
            await Assert.That(response).IsEqualTo(Response.Success("TheVersion"));
            await Assert.That(await started.Client.IsRunning(Cancel.None)).IsTrue();
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task ListAndGetReturnTheRows()
    {
        var started = await Start();
        try
        {
            var list = await started.Client.Send(new(Verb.List), Cancel.None);
            var get = await started.Client.Send(new(Verb.Get, "gh/Verify/test.yml/feature/inline"), Cancel.None);
            var missing = await started.Client.Send(new(Verb.Get, "nope"), Cancel.None);
            await Assert.That(missing.Ok).IsFalse();
            await Verify(new { list = list.Body, get = get.Body });
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task SummaryAndConnections()
    {
        var started = await Start();
        try
        {
            var summary = await started.Client.Send(new(Verb.Summary), Cancel.None);
            var connections = await started.Client.Send(new(Verb.Connections), Cancel.None);
            await Verify(new { summary = summary.Body, connections = connections.Body });
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task ShowAndQuitDriveTheWindow()
    {
        var started = await Start();
        try
        {
            await started.Client.Send(new(Verb.Show), Cancel.None);
            await started.Client.Send(new(Verb.Quit), Cancel.None);
            await Assert.That(started.Windows).IsEquivalentTo([WindowCommand.Show]);
            await Assert.That(started.Host.State.Exit).IsTrue();
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task OpenUsesTheRequestedLink()
    {
        var started = await Start();
        try
        {
            var pr = await started.Client.Send(new(Verb.Open, "gh/Verify/test.yml/feature/inline", "pr"), Cancel.None);
            var none = await started.Client.Send(new(Verb.Open, "jenkins/nightly/", "pr"), Cancel.None);
            await Assert.That(pr.Body).IsEqualTo("https://github.com/VerifyTests/Verify/pull/42");
            await Assert.That(none.Ok).IsFalse();
            await Assert.That(started.Opened).IsEquivalentTo(["https://github.com/VerifyTests/Verify/pull/42"]);
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task RetryGoesThroughTheProvider()
    {
        var started = await Start();
        try
        {
            started.Host.Mutate(_ => _ with
            {
                Builds = _.Builds.Replace(
                    _.Builds.Single(build => build.Key == "gh/Verify/test.yml/feature/inline"),
                    _.Builds.Single(build => build.Key == "gh/Verify/test.yml/feature/inline") with { ProviderRef = "VerifyTests/Verify|77|failure" })
            });
            var response = await started.Client.Send(new(Verb.Retry, "gh/Verify/test.yml/feature/inline"), Cancel.None);
            await Assert.That(response).IsEqualTo(Response.Success("Retried"));
            await Assert.That(started.Host.State.Status).IsEqualTo("Retried test.yml #77");
            var running = await started.Client.Send(new(Verb.Retry, "gh/DiffEngine/test.yml/main"), Cancel.None);
            await Assert.That(running).IsEqualTo(Response.Error("That build cannot be retried"));
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task SecondBindFails()
    {
        var started = await Start();
        try
        {
            await Assert.That(LocalServer.TryBind(started.Server.Port, out _)).IsFalse();
        }
        finally
        {
            await Stop(started);
        }
    }

    [Test]
    public async Task NothingListeningIsUnreachable()
    {
        await Assert.That(LocalServer.TryBind(0, out var server)).IsTrue();
        var port = server!.Port;
        server.Dispose();
        var client = new ProtocolClient(port);
        await Assert.That(await client.IsRunning(Cancel.None)).IsFalse();
        await Assert.That(async () => await client.Send(new(Verb.List), Cancel.None)).Throws<TrayUnreachableException>();
    }

}
