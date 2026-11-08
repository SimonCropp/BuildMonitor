/// <summary>
/// wait_for_build against an in-process tray. The tray's state is changed between one look and the
/// next from the wait's own pause callback, with no pause between looks, so each test is a script of
/// polls rather than a race against a clock. A wait that looks once more than its script has fails
/// there, rather than spinning until its timeout.
/// </summary>
public class WaitForBuildTests
{
    const string diffEngine = "gh/DiffEngine/test.yml/main";
    const string verifyMain = "gh/Verify/test.yml/main";
    const string inline = "gh/Verify/test.yml/feature/inline";
    const string dependabot = "gh/Verify/test.yml/dependabot/nuget/src/Polyfill-9.1.0";

    static TimeSpan timeout = TimeSpan.FromMinutes(1);

    static (MonitorTools Tools, SessionHost Host) Create(SessionState? state = null)
    {
        var host = new SessionHost(state ?? Fixtures.WithBuilds());
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
        var tools = new MonitorTools(new InProcessClient(handler))
        {
            WaitInterval = TimeSpan.Zero
        };
        return (tools, host);
    }

    /// <summary>
    /// The callback a wait is handed: before its Nth pause it runs the Nth step, and records what
    /// it said it was waiting on.
    /// </summary>
    static Action<TimeSpan, string> Steps(List<string> said, params Action[] steps) =>
        (_, message) =>
        {
            if (said.Count == steps.Length)
            {
                throw new InvalidOperationException($"Waited once more than the test expected: {message}");
            }

            steps[said.Count]();
            said.Add(message);
        };

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

    /// <summary>
    /// Another run of <paramref name="key"/>'s lane, newer than the ones it has.
    /// </summary>
    static void Start(SessionHost host, string key, string run, BuildStatus status) =>
        host.Mutate(_ =>
        {
            var lane = _.Builds.First(_ => _.HasKey(key));
            return _ with
            {
                Builds = _.Builds.Add(
                    lane with
                    {
                        RunNumber = run,
                        Status = status,
                        Queued = Fixtures.Now,
                        Started = Fixtures.Now,
                        Finished = null
                    })
            };
        });

    static void Nothing()
    {
    }

    [Test]
    public async Task AFinishedBuildIsReturnedAtOnce()
    {
        var (tools, _) = Create();
        var said = new List<string>();
        var result = await tools.WaitForBuild(inline, false, timeout, Steps(said), Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("77");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Failed));
        await Assert.That(said).IsEmpty();
    }

    [Test]
    public async Task ARunningBuildIsWaitedOnUntilItFinishes()
    {
        var (tools, host) = Create();
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            timeout,
            Steps(
                said,
                Nothing,
                () => Finish(host, diffEngine, "1234", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("1234");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
        await Assert.That(said).IsEquivalentTo(
        [
            "test.yml 1234: Running, 03:00 left",
            "test.yml 1234: Running, 03:00 left"
        ]);
    }

    /// <summary>
    /// A push that starts the next run as this one finishes moves the key on to the new run. The
    /// wait answers for the run it was asked about, not the one that took its place.
    /// </summary>
    [Test]
    public async Task TheRunIsFollowedPastANewerOneOnItsBranch()
    {
        var (tools, host) = Create();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            timeout,
            Steps(
                [],
                () =>
                {
                    Finish(host, diffEngine, "1234", BuildStatus.Failed);
                    Start(host, diffEngine, "1235", BuildStatus.Running);
                }),
            Cancel.None);
        await Assert.That((await tools.GetBuild(diffEngine, Cancel.None)).Run).IsEqualTo("1235");
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("1234");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Failed));
    }

    /// <summary>
    /// A pull request's run is listed only while it is running, queued or failed, so one that
    /// passed is no longer a build the key finds. It is still among the pipeline's runs.
    /// </summary>
    [Test]
    public async Task APullRequestRunThatPassedIsFoundAfterLeavingTheList()
    {
        var (tools, host) = Create(Fixtures.WithLanes());
        var result = await tools.WaitForBuild(
            dependabot,
            false,
            timeout,
            Steps(
                [],
                () => Finish(host, dependabot, "80", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(async () => await tools.GetBuild(dependabot, Cancel.None)).Throws<InvalidOperationException>();
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("80");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
    }

    [Test]
    public async Task TheTimeoutReturnsTheBuildAsItStands()
    {
        var (tools, _) = Create();
        var said = new List<string>();
        var result = await tools.WaitForBuild(diffEngine, false, TimeSpan.Zero, Steps(said), Cancel.None);
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Running));
        await Assert.That(said).IsEmpty();
    }

    [Test]
    public async Task CancellingStopsTheWait()
    {
        var (tools, _) = Create();
        using var source = new CancelSource();
        await source.CancelAsync();
        await Assert.That(async () => await tools.WaitForBuild(diffEngine, false, timeout, null, source.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>
    /// Right after a push the tray still shows the last run, finished. Waiting for the next run
    /// passes over it to the push's own, rather than answering with the last one's result.
    /// </summary>
    [Test]
    public async Task NextRunPassesOverTheFinishedRunToTheOneAfter()
    {
        var (tools, host) = Create();
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            verifyMain,
            true,
            timeout,
            Steps(
                said,
                Nothing,
                () => Start(host, verifyMain, "79", BuildStatus.Queued),
                () => Finish(host, verifyMain, "79", BuildStatus.Failed)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("79");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Failed));
        await Assert.That(said[0]).IsEqualTo("Waiting for the run after test.yml 76");
        await Assert.That(said[2]).StartsWith("test.yml 79: Queued");
    }

    /// <summary>
    /// A retry on a service that keeps the run's number goes again under the same one.
    /// </summary>
    [Test]
    public async Task NextRunWaitsOnARetryUnderTheSameNumber()
    {
        var (tools, host) = Create();
        var result = await tools.WaitForBuild(
            inline,
            true,
            timeout,
            Steps(
                [],
                Nothing,
                () => Change(host, inline, "77", _ => _ with
                {
                    Status = BuildStatus.Running,
                    Started = Fixtures.Now,
                    Finished = null
                }),
                () => Finish(host, inline, "77", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("77");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
    }

    /// <summary>
    /// A retry short enough to start and finish between two polls is never seen running. It
    /// finished at another time, which is what says it is not the run the wait began on.
    /// </summary>
    [Test]
    public async Task NextRunSeesARetryThatRanBetweenTwoPolls()
    {
        var (tools, host) = Create();
        var result = await tools.WaitForBuild(
            inline,
            true,
            timeout,
            Steps(
                [],
                () => Finish(host, inline, "77", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
    }

    /// <summary>
    /// A run still going when the wait begins is waited on, not passed over: after a refresh the
    /// tray may already show the push's run.
    /// </summary>
    [Test]
    public async Task NextRunWaitsOnARunStillGoing()
    {
        var (tools, host) = Create();
        var result = await tools.WaitForBuild(
            diffEngine,
            true,
            timeout,
            Steps(
                [],
                () => Finish(host, diffEngine, "1234", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(result.Build.Run).IsEqualTo("1234");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
    }

    /// <summary>
    /// Timing out while waiting for the next run returns the last one, finished, which the flag
    /// says is not the answer.
    /// </summary>
    [Test]
    public async Task NextRunTimesOutWithTheLastRunFlagged()
    {
        var (tools, _) = Create();
        var result = await tools.WaitForBuild(inline, true, TimeSpan.Zero, Steps([]), Cancel.None);
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Build.Run).IsEqualTo("77");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Failed));
    }

    [Test]
    public async Task AnUnknownKeyFails()
    {
        var (tools, _) = Create();
        await Assert.That(async () => await tools.WaitForBuild("gh/Nope/test.yml/main", false, timeout, null, Cancel.None))
            .Throws<InvalidOperationException>();
    }
}
