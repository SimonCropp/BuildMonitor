public class GoCdProviderTests
{
    const string server = "https://gocd.example.com";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{server}/go/api/dashboard",
                """{"_embedded":{"pipeline_groups":[{"name":"apps","pipelines":["web","api"]}],"pipelines":[]}}""")
            .Get(
                $"{server}/go/api/pipelines/web/history?page_size=10",
                """
                {"pipelines":[
                  {"name":"web","counter":42,"label":"42","scheduled_date":1767268500000,"build_cause":{"trigger_message":"modified by Simon","material_revisions":[{"material":{"type":"Git","description":"URL: https://github.com/x/web, Branch: main"},"modifications":[{"revision":"abc123","comment":"Fix","user_name":"Simon <simon@example.com>"}]}]},"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"jobs":[{"name":"compile","state":"Completed","result":"Passed","scheduled_date":1767268500000}]},{"name":"test","counter":"1","status":"Building","result":"Unknown","scheduled":true,"jobs":[{"name":"unit","state":"Building","result":"Unknown","scheduled_date":1767268800000}]}]},
                  {"name":"web","counter":41,"label":"41","scheduled_date":1767261300000,"build_cause":{"material_revisions":[{"material":{"type":"Git","description":"URL: https://github.com/x/web, Branch: main"},"modifications":[{"revision":"def456","comment":"Break","user_name":"Simon"}]}]},"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"jobs":[{"name":"compile","state":"Completed","result":"Passed","scheduled_date":1767261300000}]},{"name":"test","counter":"2","status":"Failed","result":"Failed","scheduled":true,"jobs":[{"name":"unit","state":"Completed","result":"Failed","scheduled_date":1767261600000}]}]}
                ]}
                """)
            .Get($"{server}/go/api/pipelines/api/history?page_size=10", """{"pipelines":[]}""");

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gocd", ProviderTestHelpers.Context("gocd", handler, server));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task HistoryAsksForTheSmallestPageGoCdAcceptsAndKeepsFive()
    {
        var instances = string.Join(',', Enumerable.Range(1, 10).Select(_ => $$"""{"name":"web","counter":{{_}},"stages":[]}"""));
        var handler = new FakeHttpHandler()
            .Get($"{server}/go/api/pipelines/web/history?page_size=10", $$"""{"pipelines":[{{instances}}]}""");
        var context = ProviderTestHelpers.Context("gocd", handler, server);
        var pipeline = new Pipeline("web", "web", "apps", "apps", $"{server}/go/pipeline/activity/web");
        var builds = await ProviderTestHelpers.Provider("gocd").FetchBuilds(context, [pipeline], 5, Cancel.None);
        await Assert.That(builds.Count).IsEqualTo(5);
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {server}/go/api/pipelines/web/history?page_size=10");
    }

    [Test]
    public async Task RecentActivityReadsCountersAndStageStatusesFromTheDashboard()
    {
        var handler = new FakeHttpHandler()
            .Get(
                $"{server}/go/api/dashboard",
                """
                {"_embedded":{"pipeline_groups":[{"name":"apps","pipelines":["web","api"]}],"pipelines":[
                  {"name":"web","last_updated_timestamp":1767268800000,"_embedded":{"instances":[{"label":"42","counter":42,"scheduled_at":"2026-01-01T11:55:00Z","_embedded":{"stages":[{"name":"build","counter":"1","status":"Passed"},{"name":"test","counter":"1","status":"Building"}]}}]}},
                  {"name":"api","last_updated_timestamp":1767268800000,"_embedded":{"instances":[]}}
                ]}}
                """);
        var context = ProviderTestHelpers.Context("gocd", handler, server);
        var activity = await ProviderTestHelpers.Provider("gocd").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!["web"]).IsEqualTo("42:build=Passed,test=Building");
        await Assert.That(activity["api"]).IsEqualTo("");
    }

    [Test]
    public async Task ADashboardStillLoadingFailsThePollRatherThanEmptyingIt()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", $"{server}/go/api/dashboard", """{"message":"Dashboard is being processed, this may take a few seconds. Please check back later."}""", HttpStatusCode.Accepted);
        var context = ProviderTestHelpers.Context("gocd", handler, server);
        await Assert.That(async () => await ProviderTestHelpers.Provider("gocd").DiscoverPipelines(context, Cancel.None)).Throws<HttpRequestException>();
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("POST", $"{server}/go/api/stages/web/41/test/2/run-failed-jobs", "{}", HttpStatusCode.Accepted)
            .Map("POST", $"{server}/go/api/stages/web/42/test/1/cancel", "{}");
        var context = ProviderTestHelpers.Context("gocd", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gocd", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("gocd");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "41"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "42"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://gocd.example.com/go/api/stages/web/41/test/2/run-failed-jobs
                  X-GoCD-Confirm: true,
                  POST https://gocd.example.com/go/api/stages/web/42/test/1/cancel
                  X-GoCD-Confirm: true
                ]
                """);
    }

    [Test]
    public async Task FetchLogReadsTheConsolesOfTheFailedJobs()
    {
        var handler = Handler()
            .Get(
                $"{server}/go/api/pipelines/web/41",
                """{"name":"web","counter":41,"stages":[{"name":"build","counter":"1","result":"Passed","jobs":[{"name":"compile","result":"Passed"}]},{"name":"test","counter":"2","result":"Failed","jobs":[{"name":"unit","result":"Failed"},{"name":"lint","result":"Passed"}]}]}""")
            .Get($"{server}/go/files/web/41/test/2/unit/cruise-output/console.log", "[go] Task: ./test.sh failed\n");
        var context = ProviderTestHelpers.Context("gocd", handler, server);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gocd", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("gocd").FetchLog(context, builds.Single(_ => _.RunNumber == "41"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> test / unit <==\n[go] Task: ./test.sh failed");
        await Assert.That(handler.RequestHeaders[^1].Accept.ToString()).IsEqualTo("*/*");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            $"GET {server}/go/api/pipelines/web/41",
            $"GET {server}/go/files/web/41/test/2/unit/cruise-output/console.log"
        ]);
    }

    /// <summary>
    /// 42 is running its test stage, 41 failed it, and 40 passed, so a retry of 40 schedules the
    /// pipeline again, which needs its first stage.
    /// </summary>
    [Test]
    [Arguments(true, true, true, true, true, true)]
    [Arguments(false, true, true, false, false, false)]
    [Arguments(true, false, true, true, true, false)]
    [Arguments(true, true, false, false, false, true)]
    public async Task RightsOnTheGroupAndTheStagesDecide(bool group, bool firstStage, bool testStage, bool cancelRunning, bool retryFailed, bool retryPassed)
    {
        var test = testStage.ToString().ToLowerInvariant();
        var handler = new FakeHttpHandler()
            .Get(
                $"{server}/go/api/dashboard",
                $$$"""{"_embedded":{"pipeline_groups":[{"name":"apps","pipelines":["web"]}],"pipelines":[{"name":"web","can_pause":{{{group.ToString().ToLowerInvariant()}}},"can_operate":{{{firstStage.ToString().ToLowerInvariant()}}},"_embedded":{"instances":[]}}]}}""")
            .Get(
                $"{server}/go/api/pipelines/web/history?page_size=10",
                $$"""
                {"pipelines":[
                  {"name":"web","counter":42,"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"operate_permission":true,"jobs":[]},{"name":"test","counter":"1","status":"Building","result":"Unknown","scheduled":true,"operate_permission":{{test}},"jobs":[]}]},
                  {"name":"web","counter":41,"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"operate_permission":true,"jobs":[]},{"name":"test","counter":"2","status":"Failed","result":"Failed","scheduled":true,"operate_permission":{{test}},"jobs":[]}]},
                  {"name":"web","counter":40,"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"operate_permission":true,"jobs":[]},{"name":"test","counter":"1","status":"Passed","result":"Passed","scheduled":true,"operate_permission":{{test}},"jobs":[]}]}
                ]}
                """);
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gocd", ProviderTestHelpers.Context("gocd", handler, server));
        await Assert.That(builds.Single(_ => _.RunNumber == "42").CanCancel).IsEqualTo(cancelRunning);
        await Assert.That(builds.Single(_ => _.RunNumber == "41").CanRetry).IsEqualTo(retryFailed);
        await Assert.That(builds.Single(_ => _.RunNumber == "40").CanRetry).IsEqualTo(retryPassed);
    }
}
