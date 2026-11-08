using System.Threading.Channels;

/// <summary>
/// The channel's events against an in-process tray: what is sent, when, and what it says. The
/// watches run as they do in the server, in the background, so each test waits for the event it
/// expects, for a few seconds at most.
/// </summary>
public class ChannelFeedTests
{
    const string diffEngine = "gh/DiffEngine/test.yml/main";
    const string docs = "gh/DiffEngine/docs.yml/main";
    const string inline = "gh/Verify/test.yml/feature/inline";
    const string verifyMain = "gh/Verify/test.yml/main";
    const string commit = "0123456789abcdef";

    static (ChannelFeed Feed, SessionHost Host, Channel<ChannelEvent> Events) Create()
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        var poller = new Poller(host, new MemorySecretStore(), new(), new FakeHttpHandler());
        var handler = new MessageHandler(
            host,
            poller,
            _ =>
            {
            },
            _ =>
            {
            },
            new(),
            () => Fixtures.Now);
        // Short rather than none: a watch with no pause between looks would spin a thread until the
        // test changed the build.
        var tools = new MonitorTools(new InProcessClient(handler))
        {
            WaitInterval = TimeSpan.FromMilliseconds(10)
        };
        var events = Channel.CreateUnbounded<ChannelEvent>();
        var feed = new ChannelFeed(tools, (_, cancel) => events.Writer.WriteAsync(_, cancel).AsTask());
        return (feed, host, events);
    }

    static async Task<ChannelEvent> Next(Channel<ChannelEvent> events)
    {
        using var timeout = new CancelSource(TimeSpan.FromSeconds(10));
        return await events.Reader.ReadAsync(timeout.Token);
    }

    static void Change(SessionHost host, string key, string run, Func<Build, Build> change) =>
        host.Mutate(_ =>
        {
            var build = _.Builds.Single(_ => _.HasKey(key) && _.RunNumber == run);
            return _ with
            {
                Builds = _.Builds.Replace(build, change(build))
            };
        });

    static void Finish(SessionHost host, string key, string run, BuildStatus status) =>
        Change(host, key, run, _ => _ with
        {
            Status = status,
            Finished = Fixtures.Now
        });

    static void AddDocs(SessionHost host) =>
        host.Mutate(_ => _ with
        {
            Builds = _.Builds.Add(
                Fixtures.Build(
                    Fixtures.GitHub.Id,
                    "DiffEngine/docs.yml",
                    "docs.yml",
                    "VerifyTests/DiffEngine",
                    "main",
                    "301",
                    BuildStatus.Running,
                    started: Fixtures.Now - TimeSpan.FromMinutes(1),
                    sha: commit))
        });

    static Dictionary<string, string> Meta(string kind, string key, string run, string status) =>
        new()
        {
            ["event"] = kind,
            ["key"] = key,
            ["run"] = run,
            ["status"] = status
        };

    /// <summary>
    /// A failure there when the watch began is not news; one after it is, once.
    /// </summary>
    [Test]
    public async Task ANewFailureIsSentOnce()
    {
        var (feed, host, events) = Create();
        await feed.Look(Cancel.None);
        Finish(host, diffEngine, "1234", BuildStatus.Failed);
        await feed.Look(Cancel.None);
        var sent = await Next(events);
        await Assert.That(sent.Content).IsEqualTo($"test.yml 1234 on main in VerifyTests/DiffEngine failed. Key: {diffEngine}. get_build_log with that key says why.");
        await Assert.That(sent.Meta).IsEquivalentTo(Meta(ChannelText.BuildFailed, diffEngine, "1234", "failed"));

        await feed.Look(Cancel.None);
        await Assert.That(events.Reader.TryRead(out _)).IsFalse();
    }

    [Test]
    public async Task AWatchSendsTheBuildFinishing()
    {
        var (feed, host, events) = Create();
        var build = await feed.Watch(diffEngine, false, WaitScope.Run, Cancel.None);
        await Assert.That(build.Run).IsEqualTo("1234");
        Finish(host, diffEngine, "1234", BuildStatus.Succeeded);
        var sent = await Next(events);
        await Assert.That(sent.Content).IsEqualTo($"test.yml 1234 on main in VerifyTests/DiffEngine passed. Key: {diffEngine}.");
        await Assert.That(sent.Meta).IsEquivalentTo(Meta(ChannelText.BuildFinished, diffEngine, "1234", "passed"));
    }

    [Test]
    public async Task AWatchOnAPushCountsHowItsRunsEnded()
    {
        var (feed, host, events) = Create();
        AddDocs(host);
        await feed.Watch(diffEngine, false, WaitScope.Push, Cancel.None);
        Finish(host, diffEngine, "1234", BuildStatus.Succeeded);
        Finish(host, docs, "301", BuildStatus.Failed);
        var sent = await Next(events);
        await Assert.That(sent.Content).IsEqualTo($"The push of 0123456 to VerifyTests/DiffEngine on main finished: 1 passed, 1 failed (docs.yml 301). Key: {diffEngine}.");
        await Assert.That(sent.Meta).IsEquivalentTo(Meta(ChannelText.PushFinished, diffEngine, "1234", "failed"));
    }

    [Test]
    public async Task AWatchStoppingOnFailureNamesItAndWhatIsStillGoing()
    {
        var (feed, host, events) = Create();
        AddDocs(host);
        await feed.Watch(diffEngine, false, WaitScope.PushUntilFailure, Cancel.None);
        Finish(host, docs, "301", BuildStatus.Failed);
        var sent = await Next(events);
        await Assert.That(sent.Content).IsEqualTo($"A run of the push of 0123456 to VerifyTests/DiffEngine on main failed: docs.yml 301. 1 still going. Key: {diffEngine}.");
        await Assert.That(sent.Meta).IsEquivalentTo(Meta(ChannelText.PushFailed, diffEngine, "1234", "failed"));
    }

    /// <summary>
    /// A mistyped key fails the call, while the assistant is there to hear it, rather than an event
    /// later.
    /// </summary>
    [Test]
    public async Task AnUnknownKeyFailsTheCall()
    {
        var (feed, _, events) = Create();
        var exception = await Assert.That(async () => await feed.Watch("gh/Nope/test.yml/main", false, WaitScope.Run, Cancel.None))
            .Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("No build with key gh/Nope/test.yml/main");
        await Assert.That(events.Reader.TryRead(out _)).IsFalse();
    }

    /// <summary>
    /// What fails once the watch is under way arrives as an event, since by then nobody is waiting
    /// on the call.
    /// </summary>
    [Test]
    public async Task AWatchThatCannotGoOnSaysWhy()
    {
        var (feed, _, events) = Create();
        await feed.Watch(inline, false, WaitScope.Push, Cancel.None);
        var sent = await Next(events);
        await Assert.That(sent.Content).IsEqualTo($"Could not watch {inline}: Run 77 of {inline} has no commit, so the runs of its push cannot be told apart");
        await Assert.That(sent.Meta["event"]).IsEqualTo(ChannelText.WatchFailedKind);
    }

    /// <summary>
    /// A watch ends with the server, and sends nothing after it.
    /// </summary>
    [Test]
    public async Task DisposingEndsTheWatches()
    {
        var (feed, host, events) = Create();
        await feed.Watch(diffEngine, false, WaitScope.Run, Cancel.None);
        feed.Dispose();
        Finish(host, diffEngine, "1234", BuildStatus.Succeeded);
        await Task.Delay(200);
        await Assert.That(events.Reader.TryRead(out _)).IsFalse();
    }

    [Test]
    public async Task WatchingSaysWhichRunItIsWatching()
    {
        var (feed, _, _) = Create();
        var finished = await feed.Watch(verifyMain, true, WaitScope.Run, Cancel.None);
        await Assert.That(ChannelText.Watching(finished, true, WaitScope.Run))
            .IsEqualTo("Watching the run after test.yml 76. An event arrives in this session when it finishes, as a channel tag.");
        var running = await feed.Watch(diffEngine, false, WaitScope.Push, Cancel.None);
        await Assert.That(ChannelText.Watching(running, false, WaitScope.Push))
            .IsEqualTo("Watching test.yml 1234 and the rest of its push. An event arrives in this session when it finishes, as a channel tag.");
        feed.Dispose();
    }

    [Test]
    public async Task ATimedOutWatchSaysHowItStood()
    {
        var (feed, _, _) = Create();
        var build = await feed.Watch(diffEngine, false, WaitScope.Run, Cancel.None);
        feed.Dispose();
        var sent = ChannelText.Finished(new(true, build), TimeSpan.FromHours(12));
        await Assert.That(sent.Content).IsEqualTo($"Stopped watching test.yml 1234 on main in VerifyTests/DiffEngine: still running after 12 hours. Key: {diffEngine}.");
        await Assert.That(sent.Meta).IsEquivalentTo(Meta(ChannelText.WatchTimedOut, diffEngine, "1234", "running"));
    }
}
