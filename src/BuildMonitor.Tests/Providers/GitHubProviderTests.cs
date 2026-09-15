public class GitHubProviderTests
{
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1",
                """
                [
                  {"full_name":"VerifyTests/DiffEngine","html_url":"https://github.com/VerifyTests/DiffEngine","archived":false,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"},
                  {"full_name":"SimonCropp/Forked","html_url":"https://github.com/SimonCropp/Forked","archived":false,"disabled":false,"fork":true,"pushed_at":"2099-01-01T00:00:00Z"},
                  {"full_name":"VerifyTests/Old","html_url":"https://github.com/VerifyTests/Old","archived":false,"disabled":false,"pushed_at":"2000-01-01T00:00:00Z"},
                  {"full_name":"VerifyTests/Archived","html_url":"https://github.com/VerifyTests/Archived","archived":true,"disabled":false,"pushed_at":"2099-01-01T00:00:00Z"}
                ]
                """)
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/workflows?per_page=100",
                """
                {"total_count":2,"workflows":[
                  {"id":10,"name":"Test","path":".github/workflows/test.yml","state":"active"},
                  {"id":11,"name":"Old","path":".github/workflows/old.yml","state":"disabled_manually"}
                ]}
                """)
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs?per_page=5",
                """
                {"total_count":3,"workflow_runs":[
                  {"id":500,"workflow_id":10,"run_number":1234,"status":"in_progress","conclusion":null,"head_branch":"main","head_sha":"0123456789abcdef","display_title":"Fix the thing","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/500","created_at":"2026-01-01T11:56:00Z","updated_at":"2026-01-01T11:57:00Z","run_started_at":"2026-01-01T11:57:00Z","actor":{"login":"SimonCropp"},"head_commit":{"message":"Fix the thing\n\nDetails"},"pull_requests":[]},
                  {"id":499,"workflow_id":10,"run_number":1233,"status":"completed","conclusion":"failure","head_branch":"feature","head_sha":"fedcba9876543210","display_title":"Feature","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/499","created_at":"2026-01-01T11:00:00Z","updated_at":"2026-01-01T11:05:00Z","run_started_at":"2026-01-01T11:00:30Z","actor":{"login":"someone"},"head_commit":{"message":"Feature"},"pull_requests":[{"number":42}]},
                  {"id":498,"workflow_id":11,"run_number":7,"status":"completed","conclusion":"success","head_branch":"main","head_sha":"aaa","display_title":"Old","html_url":"https://github.com/VerifyTests/DiffEngine/actions/runs/498","created_at":"2026-01-01T10:00:00Z","updated_at":"2026-01-01T10:05:00Z","run_started_at":"2026-01-01T10:00:00Z","pull_requests":[]}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", ProviderTestHelpers.Context("github", handler));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task HistoryLimitIsSentAsCreated()
    {
        var handler = Handler()
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs", """{"total_count":0,"workflow_runs":[]}""");
        var context = ProviderTestHelpers.Context("github", handler) with { Since = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero) };
        await ProviderTestHelpers.DiscoverAndFetch("github", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("/actions/runs?per_page=5&created=") && _.Contains("2026-08-16"))).IsTrue();
    }

    [Test]
    public async Task ForksAndCollaborationsAreDiscoveredWhenAskedFor()
    {
        var handler = new FakeHttpHandler()
            .Get(
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member&page=1",
                """[{"full_name":"SimonCropp/Forked","html_url":"https://github.com/SimonCropp/Forked","archived":false,"disabled":false,"fork":true,"pushed_at":"2099-01-01T00:00:00Z"}]""")
            .Get(
                "https://api.github.com/repos/SimonCropp/Forked/actions/workflows?per_page=100",
                """{"total_count":1,"workflows":[{"id":1,"name":"Test","path":".github/workflows/test.yml","state":"active"}]}""");
        var context = ProviderTestHelpers.Context("github", handler) with { ShowForksAndCollaborations = true };
        var pipelines = await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines.Single().RepoName).IsEqualTo("SimonCropp/Forked");
    }

    [Test]
    public async Task RetryOfAFailureRerunsFailedJobs()
    {
        var handler = Handler()
            .Map("POST", "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/rerun-failed-jobs", "", HttpStatusCode.Created);
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("github").Retry(context, builds.Single(_ => _.RunNumber == "1233"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/rerun-failed-jobs
                ]
                """);
    }

    [Test]
    public async Task CancelARun()
    {
        var handler = Handler()
            .Map("POST", "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/500/cancel", "", HttpStatusCode.Accepted);
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("github").Cancel(context, builds.Single(_ => _.RunNumber == "1234"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/500/cancel
                ]
                """);
    }

    [Test]
    public async Task FetchLogOfTheJobsThatFailedOrTimedOut()
    {
        var handler = Handler()
            .Get(
                "https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/jobs?filter=latest&per_page=100&page=1",
                """
                {"total_count":3,"jobs":[
                  {"id":71,"name":"build (ubuntu-latest)","conclusion":"success"},
                  {"id":72,"name":"build (windows-latest)","conclusion":"failure"},
                  {"id":73,"name":"docs","conclusion":"timed_out"}
                ]}
                """)
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/72/logs", "error CS1002: ; expected\n")
            .Get("https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/73/logs", "The job has exceeded the maximum execution time\n");
        var context = ProviderTestHelpers.Context("github", handler);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("github", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("github").FetchLog(context, builds.Single(_ => _.RunNumber == "1233"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> build (windows-latest) <==\nerror CS1002: ; expected\n\n==> docs <==\nThe job has exceeded the maximum execution time");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/runs/499/jobs?filter=latest&per_page=100&page=1",
            "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/72/logs",
            "GET https://api.github.com/repos/VerifyTests/DiffEngine/actions/jobs/73/logs"
        ]);
    }

    [Test]
    public async Task OwnerScopeUsesTheOrganization()
    {
        var handler = new FakeHttpHandler()
            .Get("https://api.github.com/orgs/VerifyTests/repos?per_page=100&sort=pushed&type=all&page=1", "[]");
        var context = ProviderTestHelpers.Context("github", handler, scope: ("owner", "VerifyTests"));
        var pipelines = await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines).IsEmpty();
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://api.github.com/orgs/VerifyTests/repos?per_page=100&sort=pushed&type=all&page=1
                ]
                """);
    }

    [Test]
    public async Task OwnerFallsBackToAUser()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/orgs/SimonCropp/repos?per_page=100&sort=pushed&type=all&page=1", "{}", HttpStatusCode.NotFound)
            .Get("https://api.github.com/users/SimonCropp/repos?per_page=100&sort=pushed&page=1", "[]");
        var context = ProviderTestHelpers.Context("github", handler, scope: ("owner", "SimonCropp"));
        await ProviderTestHelpers.Provider("github").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://api.github.com/orgs/SimonCropp/repos?per_page=100&sort=pushed&type=all&page=1,
                  GET https://api.github.com/users/SimonCropp/repos?per_page=100&sort=pushed&page=1
                ]
                """);
    }

    [Test]
    public async Task EnterpriseServerUsesApiV3()
    {
        var provider = ProviderTestHelpers.Provider("github");
        var address = provider.BaseAddress(new()
        {
            Id = "x",
            ProviderId = "github",
            Name = "x",
            Server = "https://github.example.com"
        });
        await Assert.That(address.ToString()).IsEqualTo("https://github.example.com/api/v3/");
    }

    [Test]
    public async Task UnauthorizedIsAnAuthException()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        var context = ProviderTestHelpers.Context("github", handler);
        var exception = await Assert.That(async () => await ProviderTestHelpers.Provider("github").Test(context, Cancel.None)).Throws<AuthException>();
        await Assert.That(exception!.Message).Contains("401");
    }

    [Test]
    public async Task ExhaustedRateLimitIsARateLimitException()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"message":"API rate limit exceeded"}""", HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "0"), ("Retry-After", "120"));
        var context = ProviderTestHelpers.Context("github", handler);
        var exception = await Assert.That(async () => await ProviderTestHelpers.Provider("github").Test(context, Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(120));
    }

    [Test]
    public async Task RecentActivityReadsTheFirstPageOfTheListing()
    {
        var handler = Handler();
        var context = ProviderTestHelpers.Context("github", handler);
        var activity = await ProviderTestHelpers.Provider("github").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!["VerifyTests/DiffEngine"]).IsEqualTo("2099-01-01T00:00:00.0000000+00:00");
        await Assert.That(handler.Requests.Single()).IsEqualTo("GET https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1");
    }

    [Test]
    public async Task RecentActivitySharesTheDiscoveryETag()
    {
        const string listing = "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member&page=1";
        var handler = new FakeHttpHandler().Map("GET", listing, "[]", HttpStatusCode.OK, ("ETag", "\"repos\""));
        var context = ProviderTestHelpers.Context("github", handler);
        var provider = ProviderTestHelpers.Provider("github");
        await provider.DiscoverPipelines(context, Cancel.None);
        handler.Map("GET", listing, "", HttpStatusCode.NotModified);
        await provider.RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(handler.Requests[^1]).IsEqualTo($"GET {listing}\n  If-None-Match: \"repos\"");
    }

    [Test]
    public async Task RecentActivityUsesTheUserListingOnceDiscoveryFoundIt()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/orgs/SimonCropp/repos?per_page=100&sort=pushed&type=all&page=1", "{}", HttpStatusCode.NotFound)
            .Map("GET", "https://api.github.com/users/SimonCropp/repos?per_page=100&sort=pushed&page=1", "[]", HttpStatusCode.OK, ("ETag", "\"user\""));
        var context = ProviderTestHelpers.Context("github", handler, scope: ("owner", "SimonCropp"));
        var provider = ProviderTestHelpers.Provider("github");
        await provider.DiscoverPipelines(context, Cancel.None);
        handler.Requests.Clear();
        await provider.RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(handler.Requests.Single()).StartsWith("GET https://api.github.com/users/SimonCropp/repos");
    }

    [Test]
    public async Task NotModifiedComesFromTheCache()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("ETag", "\"abc\""));
        var context = ProviderTestHelpers.Context("github", handler);
        var provider = ProviderTestHelpers.Provider("github");
        var first = await provider.Test(context, Cancel.None);

        handler.Map("GET", "https://api.github.com/user", "", HttpStatusCode.NotModified);
        var second = await provider.Test(context, Cancel.None);

        await Assert.That(first.Message).IsEqualTo("Signed in as simon");
        await Assert.That(second.Message).IsEqualTo("Signed in as simon");
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://api.github.com/user,
                  GET https://api.github.com/user
                  If-None-Match: "abc"
                ]
                """);
    }
}
