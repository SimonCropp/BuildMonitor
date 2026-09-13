public class PollerTests
{
    const string runsUrl = "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=5";

    static FakeHttpHandler GitHubHandler() =>
        new FakeHttpHandler()
            .Get("https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member&page=1",
                """[{"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"}]""")
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100",
                """{"total_count":1,"workflows":[{"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"}]}""")
            .Map("GET", runsUrl,
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
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, GitHubHandler(), null);

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
        var poller = new ConnectionPoller(Fixtures.GitHub.Id, host, secrets, history, handler, null);
        await poller.PollOnce(Cancel.None);

        // An unchanged repository answers an empty 304; the rows come from the body the first poll kept.
        handler.Map("GET", runsUrl, "", HttpStatusCode.NotModified);
        var health = await poller.PollOnce(Cancel.None);

        await Assert.That(health).IsEqualTo(ConnectionHealth.Ok);
        await Assert.That(handler.Requests[^1]).IsEqualTo($"GET {runsUrl}\n  If-None-Match: \"runs\"");
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
