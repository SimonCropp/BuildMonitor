public class GoCdProviderTests
{
    const string server = "https://gocd.example.com";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{server}/go/api/dashboard",
                """{"_embedded":{"pipeline_groups":[{"name":"apps","pipelines":["web","api"]}],"pipelines":[]}}""")
            .Get(
                $"{server}/go/api/pipelines/web/history?page_size=5",
                """
                {"pipelines":[
                  {"name":"web","counter":42,"label":"42","scheduled_date":1767268500000,"build_cause":{"trigger_message":"modified by Simon","material_revisions":[{"material":{"type":"Git","description":"URL: https://github.com/x/web, Branch: main"},"modifications":[{"revision":"abc123","comment":"Fix","user_name":"Simon <simon@example.com>"}]}]},"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"jobs":[{"name":"compile","state":"Completed","result":"Passed","scheduled_date":1767268500000}]},{"name":"test","counter":"1","status":"Building","result":"Unknown","scheduled":true,"jobs":[{"name":"unit","state":"Building","result":"Unknown","scheduled_date":1767268800000}]}]},
                  {"name":"web","counter":41,"label":"41","scheduled_date":1767261300000,"build_cause":{"material_revisions":[{"material":{"type":"Git","description":"URL: https://github.com/x/web, Branch: main"},"modifications":[{"revision":"def456","comment":"Break","user_name":"Simon"}]}]},"stages":[{"name":"build","counter":"1","status":"Passed","result":"Passed","scheduled":true,"jobs":[{"name":"compile","state":"Completed","result":"Passed","scheduled_date":1767261300000}]},{"name":"test","counter":"2","status":"Failed","result":"Failed","scheduled":true,"jobs":[{"name":"unit","state":"Completed","result":"Failed","scheduled_date":1767261600000}]}]}
                ]}
                """)
            .Get($"{server}/go/api/pipelines/api/history?page_size=5", """{"pipelines":[]}""");

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("gocd", ProviderTestHelpers.Context("gocd", handler, server));
        await Verify(new { builds, handler.Requests });
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
}
