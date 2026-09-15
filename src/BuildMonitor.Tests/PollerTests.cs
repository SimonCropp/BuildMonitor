public class PollerTests
{
    const string runsUrl = "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=5";

    // The poller asks for runs since the history cutoff, a date in the query, so a canned response
    // is keyed by the path, which the handler falls back to, and a request is matched by its start.
    static string Path(string url) =>
        url[..url.IndexOf('?')];

    static FakeHttpHandler GitHubHandler() =>
        new FakeHttpHandler()
            .Get("https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                """[{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"}]""")
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100",
                """{"total_count":1,"workflows":[{"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"}]}""")
            .Map("GET", Path(runsUrl),
                """
                {"total_count":2,"workflow_runs":[
                  {"id":500,"workflow_id":10,"run_number":2,"status":"in_progress","head_branch":"main","html_url":"https://github.com/x/500","created_at":"2026-01-01T11:56:00Z","updated_at":"2026-01-01T11:57:00Z","run_started_at":"2026-01-01T11:57:00Z","pull_requests":[]},
                  {"id":499,"workflow_id":10,"run_number":1,"status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.com/x/499","created_at":"2026-01-01T11:00:00Z","updated_at":"2026-01-01T11:05:00Z","run_started_at":"2026-01-01T11:00:00Z","pull_requests":[]}
                ]}
                """,
                HttpStatusCode.OK,
                ("ETag", "\"runs\""));

    static (SessionHost Host, MemorySecretStore Secrets, DurationHistory History) Setup(bool withToken = true)
    {
        var settings = new Settings { Connections = [Fixtures.GitHub] };
        var host = new SessionHost(SessionState.Start(settings));
        var secrets = new MemorySecretStore();
        if (withToken)
        {
            secrets.Write(SecretKeys.Token(Fixtures.GitHub.Id), "token");
        }

        return (host, secrets, new());
    }

    [Test]
    public async Task ASuccessfulPollAppliesBuildsAndRecordsDurations()
    {
        var (host, secrets, history) = Setup();
        // The runs are from the day of Fixtures.Now, so the clock is too, or they fall outside the history.
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, GitHubHandler(), null, () => Fixtures.Now);

        var health = await poller.PollOnce(Cancel.None);

        await Assert.That(health).IsEqualTo(ConnectionHealth.Ok);
        var state = host.State;
        await Assert.That(state.Builds.Length).IsEqualTo(2);
        await Assert.That(state.Connection(Fixtures.GitHub.Id)!.Health).IsEqualTo(ConnectionHealth.Ok);
        await Assert.That(state.Medians["gh/VerifyTests/DiffEngine/10"]).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task ADurationIsRecordedOnce()
    {
        var (host, secrets, history) = Setup();
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, GitHubHandler(), null);
        await poller.PollOnce(Cancel.None);
        await poller.PollOnce(Cancel.None);
        history.Record("gh/VerifyTests/DiffEngine/10", TimeSpan.FromMinutes(1));
        // One recorded run plus the manual one: median of 5 and 1 is 3, not something skewed by a
        // second recording of the same run.
        await Assert.That(history.Median("gh/VerifyTests/DiffEngine/10")).IsEqualTo(TimeSpan.FromMinutes(3));
    }

    [Test]
    public async Task ALaterPollRevalidatesWithTheETag()
    {
        var (host, secrets, history) = Setup();
        var handler = GitHubHandler();
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => Fixtures.Now);
        await poller.PollOnce(Cancel.None);

        // An unchanged repository answers an empty 304; the rows come from the body the first poll kept.
        handler.Map("GET", Path(runsUrl), "", HttpStatusCode.NotModified);
        var health = await poller.PollOnce(Cancel.None);

        await Assert.That(health).IsEqualTo(ConnectionHealth.Ok);
        // The cutoff is a day, so the second poll asks for the same URL and revalidates it.
        await Assert.That(handler.Requests[^1]).StartsWith($"GET {runsUrl}&created=");
        await Assert.That(handler.Requests[^1]).EndsWith("\n  If-None-Match: \"runs\"");
        await Assert.That(host.State.Builds.Length).IsEqualTo(2);
    }

    [Test]
    public async Task NoTokenNeedsAuth()
    {
        var (host, secrets, history) = Setup(withToken: false);
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, GitHubHandler(), null);
        var health = await poller.PollOnce(Cancel.None);
        await Assert.That(health).IsEqualTo(ConnectionHealth.NeedsAuth);
        await Assert.That(host.State.Connection(Fixtures.GitHub.Id)!.Error).IsEqualTo("No credential stored");
    }

    [Test]
    public async Task ARefusedTokenNeedsAuth()
    {
        var (host, secrets, history) = Setup();
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user/repos", """{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null);
        var health = await poller.PollOnce(Cancel.None);
        await Assert.That(health).IsEqualTo(ConnectionHealth.NeedsAuth);
    }

    [Test]
    public async Task ARateLimitIsRememberedWithItsRetryTime()
    {
        var (host, secrets, history) = Setup();
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user/repos", "{}", HttpStatusCode.TooManyRequests, ("Retry-After", "600"));
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null);
        var health = await poller.PollOnce(Cancel.None);
        await Assert.That(health).IsEqualTo(ConnectionHealth.RateLimited);
        var connection = host.State.Connection(Fixtures.GitHub.Id)!;
        await Assert.That(connection.RetryAfter).IsNotNull();
        await Assert.That(connection.RetryAfter!.Value - DateTimeOffset.UtcNow).IsGreaterThan(TimeSpan.FromMinutes(9));
    }

    [Test]
    [Arguments(1, 60)]
    [Arguments(2, 120)]
    [Arguments(3, 240)]
    public async Task ARateLimitWithoutATimeBacksOffFromAMinute(int times, int seconds)
    {
        var (host, secrets, history) = Setup();
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user/repos", "{}", HttpStatusCode.TooManyRequests);
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => Fixtures.Now);
        for (var attempt = 0; attempt < times; attempt++)
        {
            await poller.PollOnce(Cancel.None);
        }

        await Assert.That(host.State.Connection(Fixtures.GitHub.Id)!.RetryAfter).IsEqualTo(Fixtures.Now.AddSeconds(seconds));
    }

    [Test]
    public async Task AnyOtherFailureIsAnError()
    {
        var (host, secrets, history) = Setup();
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user/repos", "boom", HttpStatusCode.InternalServerError);
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null);
        var health = await poller.PollOnce(Cancel.None);
        await Assert.That(health).IsEqualTo(ConnectionHealth.Error);
        await Assert.That(host.State.Connection(Fixtures.GitHub.Id)!.Error).Contains("500");
    }

    [Test]
    public async Task ExcludedPipelinesAreNotFetched()
    {
        var (host, secrets, history) = Setup();
        host.Mutate(_ => MonitorSession.ApplySettings(_, _.Settings with { Filters = [new(FilterKind.Exact, FilterTarget.Pipeline, "Test")] }));
        var handler = GitHubHandler();
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null);
        await poller.PollOnce(Cancel.None);
        await Assert.That(handler.Requests.Any(_ => _.Contains("actions/runs"))).IsFalse();
        await Assert.That(host.State.Builds).IsEmpty();
    }

    [Test]
    public async Task TheLoopPollsAtStartAndOnRefresh()
    {
        var (host, secrets, history) = Setup();
        var handler = GitHubHandler();
        await using var poller = new Poller(host, secrets, history, handler);
        poller.Start();
        await WaitFor(() => host.State.Connection(Fixtures.GitHub.Id)!.LastPolled is not null);
        var runs = handler.Requests.Count(_ => _.Contains("actions/runs"));

        poller.Refresh(Fixtures.GitHub.Id);
        await WaitFor(() => handler.Requests.Count(_ => _.Contains("actions/runs")) > runs);
    }

    [Test]
    public async Task SyncStopsRemovedConnections()
    {
        var (host, secrets, history) = Setup();
        await using var poller = new Poller(host, secrets, history, GitHubHandler());
        poller.Start();
        await WaitFor(() => host.State.Connection(Fixtures.GitHub.Id)!.LastPolled is not null);
        poller.Sync(new());
        // Disposal waits for the loops; a stopped loop returns at once.
    }

    [Test]
    [Arguments(1, 30)]
    [Arguments(2, 60)]
    [Arguments(3, 120)]
    [Arguments(20, 600)]
    public async Task BackoffDoublesToTenMinutes(int failures, int seconds) =>
        await Assert.That(Backoff.Next(TimeSpan.FromSeconds(30), failures)).IsEqualTo(TimeSpan.FromSeconds(seconds));

    const string busyRuns = "https://api.github.com/repos/VerifyTests/Busy/actions/runs?per_page=5";
    const string quietRuns = "https://api.github.com/repos/VerifyTests/Quiet/actions/runs?per_page=5";

    // Busy has a run a minute in with no estimate, so it is finishing and due every ten seconds.
    // Quiet last built a day before, so it waits the five minute idle cap.
    static FakeHttpHandler TwoRepositories() =>
        new FakeHttpHandler()
            .Get("https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                """
                [
                  {"full_name":"VerifyTests/Busy","html_url":"https://github.com/VerifyTests/Busy","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"},
                  {"full_name":"VerifyTests/Quiet","html_url":"https://github.com/VerifyTests/Quiet","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"}
                ]
                """)
            .Get("https://api.github.com/repos/VerifyTests/Busy/actions/workflows?per_page=100",
                """{"total_count":1,"workflows":[{"id":1,"name":"Busy","path":".github/workflows/busy.yml","state":"active"}]}""")
            .Get("https://api.github.com/repos/VerifyTests/Quiet/actions/workflows?per_page=100",
                """{"total_count":1,"workflows":[{"id":2,"name":"Quiet","path":".github/workflows/quiet.yml","state":"active"}]}""")
            .Get(Path(busyRuns),
                """{"total_count":1,"workflow_runs":[{"id":10,"workflow_id":1,"run_number":5,"status":"in_progress","head_branch":"main","html_url":"https://github.com/x/10","created_at":"2026-01-01T11:59:00Z","updated_at":"2026-01-01T11:59:30Z","run_started_at":"2026-01-01T11:59:00Z","pull_requests":[]}]}""")
            .Get(Path(quietRuns),
                """{"total_count":1,"workflow_runs":[{"id":20,"workflow_id":2,"run_number":3,"status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.com/x/20","created_at":"2025-12-31T12:00:00Z","updated_at":"2025-12-31T12:05:00Z","run_started_at":"2025-12-31T12:00:00Z","pull_requests":[]}]}""");

    static int Fetches(FakeHttpHandler handler, string url) =>
        handler.Requests.Count(_ => _.StartsWith($"GET {url}", StringComparison.Ordinal));

    [Test]
    public async Task OnlyDueRepositoriesAreFetched()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        await Assert.That(poller.WakeAt!.Value - now).IsBetween(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12));
        handler.Requests.Clear();

        now = now.AddSeconds(40);
        await poller.PollDue(Cancel.None);

        await Assert.That(Fetches(handler, busyRuns)).IsEqualTo(1);
        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(0);
    }

    [Test]
    public async Task AFailingRepositoryDoesNotHideTheOthers()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories().Map("GET", Path(quietRuns), "boom", HttpStatusCode.InternalServerError);
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => Fixtures.Now);

        var health = await poller.PollOnce(Cancel.None);

        await Assert.That(health).IsEqualTo(ConnectionHealth.Error);
        await Assert.That(host.State.Builds.Select(_ => _.RunNumber)).IsEquivalentTo(["5"]);
        await Assert.That(host.State.Connection(Fixtures.GitHub.Id)!.Error).StartsWith("1 of 2 failed: 500");
    }

    [Test]
    public async Task AFailingRepositoryBacksOffAlone()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories().Map("GET", Path(quietRuns), "boom", HttpStatusCode.InternalServerError);
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);

        // First failure: retried after about thirty seconds, alongside the busy repository.
        now = now.AddSeconds(40);
        handler.Requests.Clear();
        await poller.PollDue(Cancel.None);
        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(1);

        // Second failure: about a minute, while the busy repository keeps its ten seconds.
        now = now.AddSeconds(20);
        handler.Requests.Clear();
        await poller.PollDue(Cancel.None);
        await Assert.That(Fetches(handler, busyRuns)).IsEqualTo(1);
        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(0);
    }

    [Test]
    public async Task ANudgeFetchesOnlyThatRepository()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        handler.Requests.Clear();

        now = now.AddSeconds(3);
        poller.Nudge("VerifyTests/Quiet/2");
        await poller.PollDue(Cancel.None);

        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(1);
        await Assert.That(Fetches(handler, busyRuns)).IsEqualTo(0);
    }

    [Test]
    public async Task ARefreshFetchesEveryRepository()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        handler.Requests.Clear();

        now = now.AddSeconds(3);
        poller.Refresh();
        await poller.PollDue(Cancel.None);

        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(1);
        await Assert.That(Fetches(handler, busyRuns)).IsEqualTo(1);
    }

    [Test]
    public async Task NothingIsRequestedDuringARateLimitPauseEvenOnRefresh()
    {
        var (host, secrets, history) = Setup();
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user/repos", "{}", HttpStatusCode.TooManyRequests, ("Retry-After", "600"));
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        handler.Requests.Clear();

        now = now.AddSeconds(30);
        poller.Refresh();
        var health = await poller.PollDue(Cancel.None);

        await Assert.That(handler.Requests).IsEmpty();
        await Assert.That(health).IsEqualTo(ConnectionHealth.RateLimited);
    }

    [Test]
    public async Task AFilterAddedLaterStopsFetchingAtOnce()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        host.Mutate(_ => MonitorSession.ApplySettings(_, _.Settings with { Filters = [new(FilterKind.Exact, FilterTarget.Pipeline, "Quiet")] }));
        handler.Requests.Clear();

        now = now.AddSeconds(3);
        await poller.PollOnce(Cancel.None);

        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(0);
        await Assert.That(Fetches(handler, busyRuns)).IsEqualTo(1);
    }

    const string listing = "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1";

    [Test]
    public async Task TheFirstProbeOnlyRecords()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        handler.Requests.Clear();

        // The first cycle discovered, which counts as the probe's turn; the next comes an interval later.
        now = now.AddSeconds(31);
        await poller.PollDue(Cancel.None);

        await Assert.That(Fetches(handler, listing)).IsEqualTo(1);
        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(0);
    }

    [Test]
    public async Task APushNudgesItsRepository()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        // The first probe records what it sees.
        now = now.AddSeconds(31);
        await poller.PollDue(Cancel.None);

        handler.Get(
            listing,
            """
            [
              {"full_name":"VerifyTests/Busy","html_url":"https://github.com/VerifyTests/Busy","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"},
              {"full_name":"VerifyTests/Quiet","html_url":"https://github.com/VerifyTests/Quiet","archived":false,"disabled":false,"pushed_at":"2099-01-02T00:00:00Z"}
            ]
            """);
        handler.Requests.Clear();
        now = now.AddSeconds(31);
        await poller.PollDue(Cancel.None);

        await Assert.That(Fetches(handler, quietRuns)).IsEqualTo(1);
    }

    [Test]
    public async Task AFailingProbeDoesNotFailTheConnection()
    {
        var (host, secrets, history) = Setup();
        var handler = TwoRepositories();
        var now = Fixtures.Now;
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null, () => now);
        await poller.PollOnce(Cancel.None);
        handler.Map("GET", listing, "boom", HttpStatusCode.InternalServerError);

        now = now.AddSeconds(31);
        var health = await poller.PollDue(Cancel.None);

        await Assert.That(health).IsEqualTo(ConnectionHealth.Ok);
        await Assert.That(host.State.Connection(Fixtures.GitHub.Id)!.Error).IsNull();
    }

    static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException();
            }

            await Task.Delay(20);
        }
    }
}
