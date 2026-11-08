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
    const string docs = "gh/DiffEngine/docs.yml/main";
    // The commit DiffEngine's main #1234 ran, which a push's other runs share.
    const string commit = "0123456789abcdef";

    static TimeSpan timeout = TimeSpan.FromMinutes(1);

    static (MonitorTools Tools, SessionHost Host) Create(SessionState? state = null)
    {
        var (tools, host, _) = CreateWithClient(state);
        return (tools, host);
    }

    static (MonitorTools Tools, SessionHost Host, InProcessClient Client) CreateWithClient(SessionState? state = null, Func<Message, Response?>? answer = null)
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
        var client = new InProcessClient(handler, answer);
        var tools = new MonitorTools(client)
        {
            WaitInterval = TimeSpan.Zero
        };
        return (tools, host, client);
    }

    static IEnumerable<string> Nudged(InProcessClient client) =>
        client.Sent
            .Where(_ => _.Verb == Verb.Nudge)
            .Select(_ => _.Key ?? "");

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
    static void Start(SessionHost host, string key, string run, BuildStatus status, string? sha = null) =>
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
                        Finished = null,
                        CommitSha = sha ?? lane.CommitSha
                    })
            };
        });

    static void Add(SessionHost host, params Build[] builds) =>
        host.Mutate(_ => _ with
        {
            Builds = _.Builds.AddRange(builds)
        });

    /// <summary>
    /// A run of one of DiffEngine's pipelines, which <see cref="GroupKey.PushOf"/> puts in the push
    /// of #1234 when it is on main at <see cref="commit"/>.
    /// </summary>
    static Build DiffEngine(string pipeline, string branch, string run, BuildStatus status, string sha) =>
        Fixtures.Build(
            Fixtures.GitHub.Id,
            $"DiffEngine/{pipeline}",
            pipeline,
            "VerifyTests/DiffEngine",
            branch,
            run,
            status,
            started: Fixtures.Now - TimeSpan.FromMinutes(1),
            sha: sha);

    static IEnumerable<(string Key, string Run, string Status)> Runs(BuildWaitDto result) =>
        result.PushRuns!.Select(_ => (_.Key, _.Run, _.Status));

    static void Nothing()
    {
    }

    [Test]
    public async Task AFinishedBuildIsReturnedAtOnce()
    {
        var (tools, _) = Create();
        var said = new List<string>();
        var result = await tools.WaitForBuild(inline, false, WaitScope.Run, timeout, Steps(said), Cancel.None);
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
            WaitScope.Run,
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
            WaitScope.Run,
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
            WaitScope.Run,
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
        var result = await tools.WaitForBuild(diffEngine, false, WaitScope.Run, TimeSpan.Zero, Steps(said), Cancel.None);
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
        await Assert.That(async () => await tools.WaitForBuild(diffEngine, false, WaitScope.Run, timeout, null, source.Token))
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
            WaitScope.Run,
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
            WaitScope.Run,
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
            WaitScope.Run,
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
            WaitScope.Run,
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
        var result = await tools.WaitForBuild(inline, true, WaitScope.Run, TimeSpan.Zero, Steps([]), Cancel.None);
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Build.Run).IsEqualTo("77");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Failed));
    }

    /// <summary>
    /// Waiting past a finished run asks the tray to poll that pipeline, once: a pipeline that had
    /// been quiet would otherwise not be polled for a while, and the wait would sit until it was.
    /// </summary>
    [Test]
    public async Task NextRunAsksTheTrayToPollThePipeline()
    {
        var (tools, host, client) = CreateWithClient();
        await tools.WaitForBuild(
            inline,
            true,
            WaitScope.Run,
            timeout,
            Steps(
                [],
                Nothing,
                () => Finish(host, inline, "77", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(Nudged(client)).IsEquivalentTo([inline]);
    }

    /// <summary>
    /// A run that is going is polled often near when it should finish already, and a finished one
    /// waited on without nextRun is answered at once.
    /// </summary>
    [Test]
    public async Task OnlyNextRunPastAFinishedRunAsksForAPoll()
    {
        var (tools, host, client) = CreateWithClient();
        await tools.WaitForBuild(inline, false, WaitScope.Run, timeout, Steps([]), Cancel.None);
        await tools.WaitForBuild(
            diffEngine,
            true,
            WaitScope.Run,
            timeout,
            Steps(
                [],
                () => Finish(host, diffEngine, "1234", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(Nudged(client)).IsEmpty();
    }

    /// <summary>
    /// A tray older than the verb answers it as unreadable. The wait goes on, only as slowly as it
    /// did before there was a nudge.
    /// </summary>
    [Test]
    public async Task ATrayThatRefusesTheNudgeStillAnswersTheWait()
    {
        var (tools, host, client) = CreateWithClient(
            answer: _ =>
            {
                if (_.Verb == Verb.Nudge)
                {
                    return Response.Error("Unreadable message");
                }

                return null;
            });
        var result = await tools.WaitForBuild(
            inline,
            true,
            WaitScope.Run,
            timeout,
            Steps(
                [],
                () => Finish(host, inline, "77", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(Nudged(client)).IsEquivalentTo([inline]);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
    }

    [Test]
    public async Task TheTrayRefusesANudgeForABuildItDoesNotHave()
    {
        var (_, _, client) = CreateWithClient();
        var known = await client.Send(new(Verb.Nudge, inline), Cancel.None);
        var unknown = await client.Send(new(Verb.Nudge, "gh/Nope/test.yml/main"), Cancel.None);
        await Assert.That(known.Ok).IsTrue();
        await Assert.That(unknown.Ok).IsFalse();
        await Assert.That(unknown.Body).IsEqualTo("No build with key gh/Nope/test.yml/main");
    }

    /// <summary>
    /// A push starts a run of each pipeline, and the wait ends when the last of them does. A run of
    /// another commit, or of the same commit on another branch, is not part of it.
    /// </summary>
    [Test]
    public async Task WholePushWaitsForEveryPipelineItsCommitStarted()
    {
        var (tools, host) = Create();
        Add(
            host,
            DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit),
            DiffEngine("docs.yml", "main", "299", BuildStatus.Succeeded, "feedface"),
            DiffEngine("test.yml", "release", "1233", BuildStatus.Running, commit));
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            WaitScope.Push,
            timeout,
            Steps(
                said,
                () => Finish(host, diffEngine, "1234", BuildStatus.Succeeded),
                () => Finish(host, docs, "301", BuildStatus.Failed)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("1234");
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
        await Assert.That(Runs(result)).IsEquivalentTo(
        [
            (docs, "301", nameof(BuildStatus.Failed)),
            (diffEngine, "1234", nameof(BuildStatus.Succeeded))
        ]);
        await Assert.That(said).IsEquivalentTo(
        [
            "0 of 2 runs of VerifyTests/DiffEngine at 0123456 finished",
            "1 of 2 runs of VerifyTests/DiffEngine at 0123456 finished"
        ]);
    }

    /// <summary>
    /// The run the key shows finishing first is not the push finishing.
    /// </summary>
    [Test]
    public async Task WholePushWaitsOnTheOthersWhenThisRunHasFinished()
    {
        var (tools, host) = Create();
        Finish(host, diffEngine, "1234", BuildStatus.Succeeded);
        Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit));
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            WaitScope.Push,
            timeout,
            Steps(
                said,
                () => Finish(host, docs, "301", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(said.Count).IsEqualTo(1);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.PushRuns!.All(_ => _.Status == nameof(BuildStatus.Succeeded))).IsTrue();
    }

    /// <summary>
    /// A push's pipelines are not all polled at once. One that shows up after the wait began is
    /// counted, rather than the wait ending on the runs it saw first.
    /// </summary>
    [Test]
    public async Task WholePushCountsARunThatShowsUpLater()
    {
        var (tools, host) = Create();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            WaitScope.Push,
            timeout,
            Steps(
                [],
                () =>
                {
                    Finish(host, diffEngine, "1234", BuildStatus.Succeeded);
                    Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit));
                },
                () => Finish(host, docs, "301", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.PushRuns!.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A skipped run did nothing and says nothing about the push, so it is left out. A run waiting
    /// on approval is unknown too, but has yet to run, so it stays.
    /// </summary>
    [Test]
    public async Task WholePushLeavesOutSkippedRuns()
    {
        var (tools, host) = Create();
        Add(
            host,
            DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit),
            DiffEngine("merge.yml", "main", "50", BuildStatus.Unknown, commit) with
            {
                StatusText = "skipped"
            },
            DiffEngine("approve.yml", "main", "7", BuildStatus.Unknown, commit) with
            {
                StatusText = "action_required"
            });
        var result = await tools.WaitForBuild(diffEngine, false, WaitScope.Push, TimeSpan.Zero, Steps([]), Cancel.None);
        await Assert.That(Runs(result)).IsEquivalentTo(
        [
            ("gh/DiffEngine/approve.yml/main", "7", nameof(BuildStatus.Unknown)),
            (docs, "301", nameof(BuildStatus.Running)),
            (diffEngine, "1234", nameof(BuildStatus.Running))
        ]);
    }

    /// <summary>
    /// The run the wait was asked about is what its push was found from, so it is listed even when
    /// it was skipped.
    /// </summary>
    [Test]
    public async Task WholePushKeepsTheRunAskedAboutWhenItWasSkipped()
    {
        var (tools, host) = Create();
        Change(host, diffEngine, "1234", _ => _ with
        {
            Status = BuildStatus.Unknown,
            StatusText = "skipped",
            Finished = Fixtures.Now
        });
        var result = await tools.WaitForBuild(diffEngine, false, WaitScope.Push, timeout, Steps([]), Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(Runs(result)).IsEquivalentTo(
        [
            (diffEngine, "1234", nameof(BuildStatus.Unknown))
        ]);
    }

    /// <summary>
    /// The first failure is the answer: the wait ends there, with the rest as they stood, rather
    /// than holding it back until the slowest run is done.
    /// </summary>
    [Test]
    public async Task StopOnFailureReturnsAtTheFirstFailure()
    {
        var (tools, host) = Create();
        Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit));
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            WaitScope.PushUntilFailure,
            timeout,
            Steps(
                said,
                () => Finish(host, docs, "301", BuildStatus.Failed)),
            Cancel.None);
        await Assert.That(said.Count).IsEqualTo(1);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Running));
        await Assert.That(Runs(result)).IsEquivalentTo(
        [
            (docs, "301", nameof(BuildStatus.Failed)),
            (diffEngine, "1234", nameof(BuildStatus.Running))
        ]);
    }

    /// <summary>
    /// A push that had failed already when the wait began is answered at once, as a finished run is.
    /// </summary>
    [Test]
    public async Task StopOnFailureAnswersAPushThatHadFailedAtOnce()
    {
        var (tools, host) = Create();
        Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Failed, commit));
        var said = new List<string>();
        var result = await tools.WaitForBuild(diffEngine, false, WaitScope.PushUntilFailure, timeout, Steps(said), Cancel.None);
        await Assert.That(said).IsEmpty();
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.PushRuns!.Count(_ => _.Status == nameof(BuildStatus.Failed))).IsEqualTo(1);
    }

    /// <summary>
    /// With nothing failing, it waits for every run, as a whole push does. A cancelled run, cancelled
    /// for a newer push or by hand, is not a failure of this one.
    /// </summary>
    [Test]
    public async Task StopOnFailureWaitsForEveryRunWhenNoneFails()
    {
        var (tools, host) = Create();
        Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit));
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            WaitScope.PushUntilFailure,
            timeout,
            Steps(
                said,
                () => Finish(host, docs, "301", BuildStatus.Cancelled),
                () => Finish(host, diffEngine, "1234", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(said.Count).IsEqualTo(2);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(Runs(result)).IsEquivalentTo(
        [
            (docs, "301", nameof(BuildStatus.Cancelled)),
            (diffEngine, "1234", nameof(BuildStatus.Succeeded))
        ]);
    }

    /// <summary>
    /// Without stopping on failure, a failure is one more finished run, and the wait goes on.
    /// </summary>
    [Test]
    public async Task WholePushGoesOnPastAFailure()
    {
        var (tools, host) = Create();
        Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Failed, commit));
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            diffEngine,
            false,
            WaitScope.Push,
            timeout,
            Steps(
                said,
                () => Finish(host, diffEngine, "1234", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(said.Count).IsEqualTo(1);
        await Assert.That(result.Build.Status).IsEqualTo(nameof(BuildStatus.Succeeded));
    }

    [Test]
    public async Task WholePushTimesOutWithEveryRunAsItStood()
    {
        var (tools, host) = Create();
        Add(host, DiffEngine("docs.yml", "main", "301", BuildStatus.Running, commit));
        var result = await tools.WaitForBuild(diffEngine, false, WaitScope.Push, TimeSpan.Zero, Steps([]), Cancel.None);
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(Runs(result)).IsEquivalentTo(
        [
            (docs, "301", nameof(BuildStatus.Running)),
            (diffEngine, "1234", nameof(BuildStatus.Running))
        ]);
    }

    /// <summary>
    /// After a push, the next run is waited for first, and then every run of its push.
    /// </summary>
    [Test]
    public async Task WholePushAfterNextRun()
    {
        var (tools, host) = Create();
        var said = new List<string>();
        var result = await tools.WaitForBuild(
            verifyMain,
            true,
            WaitScope.Push,
            timeout,
            Steps(
                said,
                Nothing,
                () =>
                {
                    Start(host, verifyMain, "79", BuildStatus.Queued, "feedface");
                    Add(
                        host,
                        Fixtures.Build(
                            Fixtures.GitHub.Id,
                            "Verify/release.yml",
                            "release.yml",
                            "VerifyTests/Verify",
                            "main",
                            "5",
                            BuildStatus.Running,
                            started: Fixtures.Now,
                            sha: "feedface"));
                },
                () => Finish(host, verifyMain, "79", BuildStatus.Succeeded),
                () => Finish(host, "gh/Verify/release.yml/main", "5", BuildStatus.Succeeded)),
            Cancel.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Build.Run).IsEqualTo("79");
        await Assert.That(result.PushRuns!.Count).IsEqualTo(2);
        await Assert.That(said[2]).IsEqualTo("0 of 2 runs of VerifyTests/Verify at feedfac finished");
    }

    /// <summary>
    /// A service that reports no commit leaves nothing to tell one push from the next, which the
    /// wait says rather than waiting on the one run as though it were the whole push.
    /// </summary>
    [Test]
    public async Task WholePushNeedsACommit()
    {
        var (tools, _) = Create();
        var exception = await Assert.That(async () => await tools.WaitForBuild(inline, false, WaitScope.Push, timeout, null, Cancel.None))
            .Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo($"Run 77 of {inline} has no commit, so the runs of its push cannot be told apart");
    }

    /// <summary>
    /// Only the user can restart a tray older than the verb, so the wait says that rather than
    /// passing on that the tray could not read the message.
    /// </summary>
    [Test]
    public async Task AnOlderTraySaysToRestartIt()
    {
        var (tools, _, _) = CreateWithClient(
            answer: _ =>
            {
                if (_.Verb == Verb.PushRuns)
                {
                    return Response.Error(LocalServer.Unreadable);
                }

                return null;
            });
        var exception = await Assert.That(async () => await tools.WaitForBuild(diffEngine, false, WaitScope.Push, timeout, null, Cancel.None))
            .Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).StartsWith("The tray running is older than this server");
    }

    [Test]
    public async Task TheTrayRefusesThePushOfARunItDoesNotHave()
    {
        var (_, _, client) = CreateWithClient();
        var response = await client.Send(new(Verb.PushRuns, diffEngine, "9999"), Cancel.None);
        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Body).IsEqualTo($"No run 9999 of {diffEngine}");
    }

    [Test]
    public async Task AnUnknownKeyFails()
    {
        var (tools, _) = Create();
        await Assert.That(async () => await tools.WaitForBuild("gh/Nope/test.yml/main", false, WaitScope.Run, timeout, null, Cancel.None))
            .Throws<InvalidOperationException>();
    }
}
