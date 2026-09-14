public class BitbucketProviderTests
{
    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://api.bitbucket.org/2.0/repositories/verify?role=member&pagelen=100&sort=-updated_on",
                """{"values":[{"slug":"diffengine","full_name":"verify/diffengine","links":{"html":{"href":"https://bitbucket.org/verify/diffengine"}}}]}""")
            .Get(
                "https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines?sort=-created_on&pagelen=5",
                """
                {"values":[
                  {"uuid":"{u1}","build_number":88,"state":{"name":"IN_PROGRESS","stage":{"name":"RUNNING"}},"target":{"type":"pipeline_ref_target","ref_type":"branch","ref_name":"main","commit":{"hash":"abc123"}},"creator":{"display_name":"Simon"},"created_on":"2026-01-01T11:55:00Z","completed_on":null},
                  {"uuid":"{u2}","build_number":87,"state":{"name":"COMPLETED","result":{"name":"FAILED"}},"target":{"type":"pipeline_pullrequest_target","ref_type":"branch","ref_name":"feature","commit":{"hash":"def456"},"pullrequest":{"id":9,"title":"Feature"}},"creator":{"display_name":"Simon"},"created_on":"2026-01-01T10:00:00Z","completed_on":"2026-01-01T10:07:00Z","duration_in_seconds":420}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("bitbucket", ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify")));
        await Verify(new { builds, handler.Requests });
    }

    [Test]
    public async Task RecentActivityReadsTheMostRecentlyUpdatedRepositories()
    {
        const string url = "https://api.bitbucket.org/2.0/repositories/verify?role=member&sort=-updated_on&pagelen=10&fields=values.slug,values.updated_on";
        var handler = new FakeHttpHandler()
            .Get(url, """{"values":[{"slug":"diffengine","updated_on":"2026-01-01T11:59:40Z"},{"slug":"empty"}]}""");
        var context = ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify"));
        var activity = await ProviderTestHelpers.Provider("bitbucket").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!.Count).IsEqualTo(1);
        await Assert.That(activity["diffengine"]).IsEqualTo("2026-01-01T11:59:40.0000000+00:00");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {url}");
    }

    [Test]
    public async Task RetryStartsANewPipelineAndCancelStops()
    {
        var handler = Handler()
            .Map("POST", "https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines", "{}", HttpStatusCode.Created)
            .Map("POST", "https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines/{u1}/stopPipeline", "", HttpStatusCode.NoContent);
        var context = ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("bitbucket", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("bitbucket");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "87"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "88"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  POST https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines
                  {"target":{"type":"pipeline_ref_target","ref_type":"branch","ref_name":"feature","commit":{"type":"commit","hash":"def456"}}},
                  POST https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines/{u1}/stopPipeline
                ]
                """);
    }
}
