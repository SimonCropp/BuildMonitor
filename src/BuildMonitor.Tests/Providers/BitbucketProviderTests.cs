public class BitbucketProviderTests
{
    const string fields = "values.uuid,values.build_number,values.state,values.target.ref_type,values.target.ref_name,values.target.source,values.target.destination,values.target.commit.hash,values.target.pullrequest.id,values.creator.uuid,values.creator.display_name,values.created_on,values.completed_on";

    const string pipelines = $"https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines?sort=-created_on&pagelen=5&fields={fields}";

    const string mainPipelines = $"https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines?sort=-created_on&pagelen=1&target.ref_type=BRANCH&target.ref_name=main&fields={fields}";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                "https://api.bitbucket.org/2.0/repositories/verify?role=member&pagelen=100&sort=-updated_on&fields=next,values.slug,values.full_name,values.links.html.href,values.mainbranch.name",
                """{"values":[{"slug":"diffengine","full_name":"verify/diffengine","links":{"html":{"href":"https://bitbucket.org/verify/diffengine"}},"mainbranch":{"name":"main"}}]}""")
            .Get(
                pipelines,
                """
                {"values":[
                  {"uuid":"{u1}","build_number":88,"state":{"name":"IN_PROGRESS","stage":{"name":"RUNNING"}},"target":{"type":"pipeline_ref_target","ref_type":"branch","ref_name":"main","commit":{"hash":"abc123"}},"creator":{"display_name":"Simon"},"created_on":"2026-01-01T11:55:00Z","completed_on":null},
                  {"uuid":"{u2}","build_number":87,"state":{"name":"COMPLETED","result":{"name":"FAILED"}},"target":{"type":"pipeline_ref_target","ref_type":"branch","ref_name":"feature","commit":{"hash":"def456"}},"creator":{"display_name":"Simon"},"created_on":"2026-01-01T10:00:00Z","completed_on":"2026-01-01T10:07:00Z","duration_in_seconds":420},
                  {"uuid":"{u3}","build_number":86,"state":{"name":"COMPLETED","result":{"name":"SUCCESSFUL"}},"target":{"type":"pipeline_pullrequest_target","source":"feature","destination":"main","commit":{"hash":"def456"},"pullrequest":{"id":9}},"creator":{"display_name":"Simon"},"created_on":"2026-01-01T09:50:00Z","completed_on":"2026-01-01T09:57:00Z"}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("bitbucket", ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify")));
        await Verify(new { builds, handler.Requests });
    }

    /// <summary>
    /// A window of pull requests, each built as its branch and as the pull request, asks for the
    /// newest pipeline on main by its ref, which leaves out the pull request pipelines targeting it.
    /// </summary>
    [Test]
    public async Task PullRequestsFillingTheWindowFetchTheMainBranchsNewestPipeline()
    {
        var handler = PullRequestsOnly()
            .Get(
                mainPipelines,
                """{"values":[{"uuid":"{u9}","build_number":80,"state":{"name":"COMPLETED","result":{"name":"SUCCESSFUL"}},"target":{"type":"pipeline_ref_target","ref_type":"branch","ref_name":"main","commit":{"hash":"999"}},"creator":{"display_name":"Simon"},"created_on":"2025-12-31T10:00:00Z","completed_on":"2025-12-31T10:07:00Z"}]}""");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("bitbucket", ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify")));
        await Assert.That(builds.Select(_ => $"{_.RunNumber} {_.Branch}")).IsEquivalentTo(["86 feature", "80 main"]);
    }

    /// <summary>
    /// A main branch with no pipeline answers empty, and the fetches within the hour do not ask
    /// again: Bitbucket allows a thousand requests an hour.
    /// </summary>
    [Test]
    public async Task ARepositoryWithNoPipelineOnItsMainBranchIsAskedOnceAnHour()
    {
        var handler = PullRequestsOnly()
            .Get(mainPipelines, """{"values":[]}""");
        var context = ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify"));
        await ProviderTestHelpers.DiscoverAndFetch("bitbucket", context);
        await ProviderTestHelpers.DiscoverAndFetch("bitbucket", context);
        await Assert.That(handler.Requests.Count(_ => _ == $"GET {mainPipelines}")).IsEqualTo(1);
    }

    static FakeHttpHandler PullRequestsOnly() =>
        Handler()
            .Get(
                pipelines,
                """{"values":[{"uuid":"{u3}","build_number":86,"state":{"name":"COMPLETED","result":{"name":"SUCCESSFUL"}},"target":{"type":"pipeline_pullrequest_target","source":"feature","destination":"main","commit":{"hash":"def456"},"pullrequest":{"id":9}},"creator":{"display_name":"Simon"},"created_on":"2026-01-01T09:50:00Z","completed_on":"2026-01-01T09:57:00Z"}]}""");

    /// <summary>
    /// A Bitbucket account id is a guid, so the name it arrives with here names it for whoever else
    /// is handed that id alone.
    /// </summary>
    [Test]
    public async Task TheCreatorsNameIsLeftForOtherConnections()
    {
        const string id = "{d1a80549-4d1f-642e-b5d5-9eca49ca5e24}";
        var handler = Handler()
            .Get(
                pipelines,
                $$$"""{"values":[{"uuid":"{u1}","build_number":88,"state":{"name":"COMPLETED","result":{"name":"FAILED"}},"creator":{"uuid":"{{{id}}}","display_name":"Simon Cropp"},"created_on":"2026-01-01T11:55:00Z"}]}""");
        var context = ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify"));
        await ProviderTestHelpers.DiscoverAndFetch("bitbucket", context);
        await Assert.That(context.Identities.Name("d1a80549-4d1f-642e-b5d5-9eca49ca5e24")).IsEqualTo("Simon Cropp");
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

    [Test]
    public async Task FetchLogOfTheFailedStepsAcceptsAnything()
    {
        var handler = Handler()
            .Get(
                "https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines/{u2}/steps",
                """{"values":[{"uuid":"{s1}","name":"Build","state":{"name":"COMPLETED","result":{"name":"SUCCESSFUL"}}},{"uuid":"{s2}","name":"Test","state":{"name":"COMPLETED","result":{"name":"FAILED"}}}]}""")
            .Get("https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines/{u2}/steps/{s2}/log", "npm ERR! Test failed.\n");
        var context = ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com", scope: ("workspace", "verify"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("bitbucket", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("bitbucket").FetchLog(context, builds.Single(_ => _.RunNumber == "87"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> Test <==\nnpm ERR! Test failed.");
        await Assert.That(handler.RequestHeaders[^1].Accept.ToString()).IsEqualTo("*/*");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines/{u2}/steps",
            "GET https://api.bitbucket.org/2.0/repositories/verify/diffengine/pipelines/{u2}/steps/{s2}/log"
        ]);
    }

    const string repository = "https://api.bitbucket.org/2.0/repositories/verify/diffengine";

    static Task<BranchFate> FateOf(FakeHttpHandler handler, string branch, string? pullRequest = null, string repositoryUrl = "https://bitbucket.org/verify/diffengine") =>
        ProviderTestHelpers.Provider("bitbucket").FateOf(ProviderTestHelpers.Context("bitbucket", handler, user: "simon@example.com"), new(repositoryUrl, branch, pullRequest), Cancel.None);

    /// <summary>
    /// A pull request is asked for its state; declined and superseded are both closed.
    /// </summary>
    [Test]
    [Arguments("OPEN", BranchFate.Open)]
    [Arguments("MERGED", BranchFate.Merged)]
    [Arguments("DECLINED", BranchFate.Closed)]
    [Arguments("SUPERSEDED", BranchFate.Closed)]
    public async Task APullRequestIsAskedForItsState(string state, BranchFate fate)
    {
        var handler = new FakeHttpHandler()
            .Get($"{repository}/pullrequests/12", $$"""{"id":12,"state":"{{state}}"}""");
        await Assert.That(await FateOf(handler, "feature/x", "12")).IsEqualTo(fate);
    }

    /// <summary>
    /// A branch is asked for with its slashes as they are, which the route takes as the name.
    /// </summary>
    [Test]
    public async Task ABranchStillThereIsOpen()
    {
        var handler = new FakeHttpHandler()
            .Get($"{repository}/refs/branches/feature/x", """{"name":"feature/x"}""");
        await Assert.That(await FateOf(handler, "feature/x")).IsEqualTo(BranchFate.Open);
    }

    [Test]
    public async Task ATagOfTheNameIsOpen()
    {
        var handler = new FakeHttpHandler()
            .Get($"{repository}/refs/tags/v1.0", """{"name":"v1.0"}""");
        await Assert.That(await FateOf(handler, "v1.0")).IsEqualTo(BranchFate.Open);
    }

    /// <summary>
    /// A repository the token cannot see is a 404 as a missing branch is, so the repository is
    /// asked for before the branch is taken as deleted.
    /// </summary>
    [Test]
    public async Task NeitherBranchNorTagInARepositoryItCanSeeIsDeleted()
    {
        var handler = new FakeHttpHandler()
            .Get(repository, """{"slug":"diffengine","full_name":"verify/diffengine"}""");
        await Assert.That(await FateOf(handler, "feature/x")).IsEqualTo(BranchFate.Deleted);
        await Assert.That(await FateOf(new(), "feature/x")).IsEqualTo(BranchFate.Unknown);
    }
}
