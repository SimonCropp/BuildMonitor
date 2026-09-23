public class AzureDevOpsProviderTests
{
    const string organization = "https://dev.azure.com/contoso";

    const string builds = $"{organization}/Web/_apis/build/builds?definitions=1,2&maxBuildsPerDefinition=5&queryOrder=queueTimeDescending&properties=TriggeredBy&api-version=7.1";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{organization}/_apis/projects?api-version=7.1&$top=100",
                """{"count":1,"value":[{"id":"p1","name":"Web"}]}""")
            .Get(
                $"{organization}/Web/_apis/pipelines?api-version=7.1",
                """{"count":2,"value":[{"id":1,"name":"CI","folder":"\\","_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build?definitionId=1"}}},{"id":2,"name":"Nightly","folder":"\\ops"}]}""")
            .Get(
                builds,
                """
                {"count":3,"value":[
                  {"id":301,"buildNumber":"20260101.3","status":"inProgress","result":null,"queueTime":"2026-01-01T11:50:00Z","startTime":"2026-01-01T11:51:00Z","sourceBranch":"refs/heads/main","sourceVersion":"abc123","reason":"individualCI","requestedFor":{"displayName":"Simon"},"definition":{"id":1,"name":"CI"},"repository":{"id":"r1","type":"TfsGit","name":"Web"},"triggerInfo":{"ci.message":"Fix"},"_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build/results?buildId=301"}}},
                  {"id":300,"buildNumber":"20260101.2","status":"completed","result":"failed","queueTime":"2026-01-01T10:50:00Z","startTime":"2026-01-01T10:51:00Z","finishTime":"2026-01-01T10:59:00Z","sourceBranch":"refs/pull/55/merge","sourceVersion":"def456","reason":"pullRequest","requestedFor":{"displayName":"Simon"},"definition":{"id":1,"name":"CI"},"repository":{"id":"VerifyTests/DiffEngine","type":"GitHub","name":"VerifyTests/DiffEngine"},"parameters":"{\"system.pullRequest.pullRequestNumber\":\"55\",\"system.pullRequest.sourceBranch\":\"feature\",\"system.pullRequest.targetBranch\":\"main\",\"system.pullRequest.isFork\":\"False\"}","triggerInfo":{"pr.sourceBranch":"feature","pr.number":"55","pr.isFork":"False"},"_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build/results?buildId=300"}}},
                  {"id":299,"buildNumber":"20260101.1","status":"completed","result":"succeeded","queueTime":"2026-01-01T10:40:00Z","startTime":"2026-01-01T10:41:00Z","finishTime":"2026-01-01T10:49:00Z","sourceBranch":"refs/pull/57/merge","sourceVersion":"a7fe96d","reason":"pullRequest","requestedFor":{"displayName":"Someone"},"definition":{"id":1,"name":"CI"},"repository":{"id":"VerifyTests/DiffEngine","type":"GitHub","name":"VerifyTests/DiffEngine"},"parameters":"{\"system.pullRequest.pullRequestNumber\":\"57\",\"system.pullRequest.sourceBranch\":\"main\",\"system.pullRequest.targetBranch\":\"main\",\"system.pullRequest.isFork\":\"True\"}","triggerInfo":{"pr.sourceBranch":"main","pr.number":"57","pr.isFork":"True"},"_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build/results?buildId=299"}}},
                  {"id":290,"buildNumber":"20251231.1","status":"completed","result":"succeeded","queueTime":"2025-12-31T01:00:00Z","startTime":"2025-12-31T01:01:00Z","finishTime":"2025-12-31T01:30:00Z","sourceBranch":"refs/heads/main","sourceVersion":"111","definition":{"id":2,"name":"Nightly"},"repository":{"id":"r1","type":"TfsGit","name":"Web"}}
                ]}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso")));
        await Verify(new
        {
            builds,
            handler.Requests
        });
    }

    [Test]
    public async Task AnExcludedProjectIsNeverListed()
    {
        // A project's definitions are a request of its own, so an excluded one is dropped before it
        // is paid for rather than after the poller has the pipelines.
        var handler = Handler();
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso")) with
        {
            Filters = [new(FilterKind.Exact, FilterTarget.Repo, "Web")]
        };
        var pipelines = await ProviderTestHelpers.Provider("azure-devops").DiscoverPipelines(context, Cancel.None);
        await Assert.That(pipelines).IsEmpty();
        await Assert.That(handler.Requests.Any(_ => _.Contains("_apis/pipelines"))).IsFalse();
    }

    [Test]
    public async Task HistoryLimitIsNotSentAsMinTime()
    {
        // minTime filters on queue time, so it would hide a build queued before the cutoff that is still running.
        var handler = Handler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"))
            with
            {
                Since = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)
            };
        await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        await Assert.That(handler.Requests.Any(_ => _.Contains("_apis/build/builds?definitions="))).IsTrue();
        await Assert.That(handler.Requests.Any(_ => _.Contains("_apis/build/builds?definitions=") && _.Contains("minTime"))).IsFalse();
    }

    /// <summary>
    /// One build queued by a service account, as an end to end suite is, carrying the property
    /// that names whoever the run is really for.
    /// </summary>
    static FakeHttpHandler Triggered(string properties) =>
        Handler()
            .Get(
                builds,
                $$"""
                  {"count":1,"value":[
                    {"id":301,"buildNumber":"20260101.3","status":"completed","result":"failed","queueTime":"2026-01-01T11:50:00Z","startTime":"2026-01-01T11:51:00Z","finishTime":"2026-01-01T11:59:00Z","sourceBranch":"refs/heads/main","requestedFor":{"displayName":"Build Service"},"definition":{"id":1,"name":"CI"},"properties":{{properties}}}
                  ]}
                  """);

    [Test]
    [Arguments("""{"TriggeredBy":{"$type":"System.String","$value":"Ada Lovelace"}}""")]
    [Arguments("""{"TriggeredBy":"Ada Lovelace"}""")]
    public async Task TriggeredByNamesTheAuthor(string properties)
    {
        var handler = Triggered(properties);
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var fetched = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        await Assert.That(fetched.Single().Author).IsEqualTo("Ada Lovelace");
    }

    /// <summary>
    /// The property holds a name. An id is not one: shown, it would fill the author column with a
    /// guid that names nobody, so the row goes on naming whoever queued the build.
    /// </summary>
    [Test]
    public async Task AnIdInTriggeredByNamesTheRequester()
    {
        var handler = Triggered("""{"TriggeredBy":"e9d8e877-ffad-6366-81bf-b5db2947d44c"}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var fetched = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        await Assert.That(fetched.Single().Author).IsEqualTo("Build Service");
    }

    /// <summary>
    /// The name of the property is whatever the pipeline that wrote it called it, so its case is
    /// not held against it.
    /// </summary>
    [Test]
    public async Task TheNameOfThePropertyIsMatchedWithoutCase()
    {
        var handler = Triggered("""{"triggeredby":"Ada Lovelace"}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var fetched = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        await Assert.That(fetched.Single().Author).IsEqualTo("Ada Lovelace");
    }

    /// <summary>
    /// A property collection is whatever the service sends, and a shape that was not an object of
    /// strings failed the whole project's fetch, taking every row of it off the screen.
    /// </summary>
    [Test]
    [Arguments("{}")]
    [Arguments("[]")]
    [Arguments("null")]
    [Arguments("\"\"")]
    [Arguments("""{"TriggeredBy":null}""")]
    [Arguments("""{"TriggeredBy":123}""")]
    [Arguments("""{"TriggeredBy":["e9d8e877-ffad-6366-81bf-b5db2947d44c"]}""")]
    [Arguments("""{"TriggeredBy":{"$type":"System.Int32","$value":5}}""")]
    [Arguments("""{"TriggeredBy":"   "}""")]
    public async Task APropertyCollectionOfAnyShapeNamesTheRequester(string properties)
    {
        var handler = Triggered(properties);
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var fetched = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        await Assert.That(fetched.Single().Author).IsEqualTo("Build Service");
    }

    static PollGroup Web() =>
        new("Web", [new("Web/1", "CI", "Web", "Web", "https://dev.azure.com/contoso/Web"), new("Web/2", "Nightly", "Web", "Web", "https://dev.azure.com/contoso/Web")]);

    [Test]
    public async Task TheFirstProbeAsksForTheNewestBuild()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":1,"value":[{"id":301,"queueTime":"2026-01-01T11:50:00Z","definition":{"id":1}}]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var activity = await ProviderTestHelpers.Provider("azure-devops").RecentActivity(context, [Web()], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!["Web"]).IsEqualTo("2026-01-01T11:50:00.0000000+00:00");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {organization}/Web/_apis/build/builds?definitions=1,2&$top=1&queryOrder=queueTimeDescending&api-version=7.1");
    }

    [Test]
    public async Task ALaterProbeAsksOnlyForBuildsQueuedSince()
    {
        const string seen = "2026-01-01T11:50:00.0000000+00:00";
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var activity = await ProviderTestHelpers.Provider("azure-devops").RecentActivity(context, [Web()], ImmutableDictionary<string, string>.Empty.Add("Web", seen), Cancel.None);
        await Assert.That(activity!["Web"]).IsEqualTo(seen);
        await Assert.That(handler.Requests.Single()).Contains("&minTime=2026-01-01T11");
    }

    [Test]
    public async Task ABuildQueuedSinceMovesTheToken()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/build/builds", """{"count":1,"value":[{"id":302,"queueTime":"2026-01-01T11:58:00Z","definition":{"id":2}}]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var activity = await ProviderTestHelpers.Provider("azure-devops").RecentActivity(context, [Web()], ImmutableDictionary<string, string>.Empty.Add("Web", "2026-01-01T11:50:00.0000000+00:00"), Cancel.None);
        await Assert.That(activity!["Web"]).IsEqualTo("2026-01-01T11:58:00.0000000+00:00");
    }

    [Test]
    public async Task RetryAndCancel()
    {
        var handler = Handler()
            .Map("PATCH", $"{organization}/Web/_apis/build/builds/300?retry=true&api-version=7.1", "{}")
            .Map("PATCH", $"{organization}/Web/_apis/build/builds/301?api-version=7.1", "{}");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("azure-devops");
        await provider.Retry(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "20260101.3"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  PATCH https://dev.azure.com/contoso/Web/_apis/build/builds/300?retry=true&api-version=7.1
                  {},
                  PATCH https://dev.azure.com/contoso/Web/_apis/build/builds/301?api-version=7.1
                  {"status":"cancelling"}
                ]
                """);
    }

    /// <summary>
    /// Azure DevOps documents no verb for its Run next, only the queue position as a field of the
    /// build that Update Build will take, so this pins the shape of the patch rather than a route.
    /// <para>
    /// On a handler of its own rather than the shared one: every other test here reads the same
    /// three builds, and a fourth in the list would move the fetch snapshot for the sake of one
    /// call.
    /// </para>
    /// </summary>
    [Test]
    public async Task RunNextMovesAQueuedBuildToTheFrontOfTheQueue()
    {
        var handler = Queued()
            .Map("PATCH", $"{organization}/Web/_apis/build/builds/302?api-version=7.1", "{}");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("azure-devops").RunNext(context, builds.Single(_ => _.Status == BuildStatus.Queued), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  PATCH https://dev.azure.com/contoso/Web/_apis/build/builds/302?api-version=7.1
                  {"queuePosition":1}
                ]
                """);
    }

    /// <summary>
    /// One build, waiting: notStarted is what Azure DevOps calls a run that has been queued and has
    /// not been given an agent.
    /// </summary>
    static FakeHttpHandler Queued() =>
        new FakeHttpHandler()
            .Get(
                $"{organization}/_apis/projects?api-version=7.1&$top=100",
                """{"count":1,"value":[{"id":"p1","name":"Web"}]}""")
            .Get(
                $"{organization}/Web/_apis/pipelines?api-version=7.1",
                """{"count":1,"value":[{"id":1,"name":"CI","folder":"\\"}]}""")
            .Get(
                $"{organization}/Web/_apis/build/builds",
                """
                {"count":1,"value":[
                  {"id":302,"buildNumber":"20260101.4","status":"notStarted","result":null,"queueTime":"2026-01-01T11:58:00Z","sourceBranch":"refs/heads/main","definition":{"id":1,"name":"CI"},"repository":{"id":"r1","type":"TfsGit","name":"Web"}}
                ]}
                """);

    [Test]
    public async Task FetchLogOfTheFailedTasksAsText()
    {
        var handler = Handler()
            .Get(
                $"{organization}/Web/_apis/build/builds/300/timeline?api-version=7.1",
                """
                {"records":[
                  {"id":"t2","parentId":"j1","type":"Task","name":"Test","result":"failed","log":{"id":5}},
                  {"id":"j1","parentId":"p1","type":"Job","name":"Build","result":"failed","log":{"id":3}},
                  {"id":"t1","parentId":"j1","type":"Task","name":"Checkout","result":"succeeded","log":{"id":4}},
                  {"id":"p1","type":"Phase","name":"Build","result":"failed"}
                ]}
                """)
            .Get($"{organization}/Web/_apis/build/builds/300/logs/5?api-version=7.1", "##[error]1 test failed\n");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("azure-devops").FetchLog(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> Build / Test <==\n##[error]1 test failed");
        await Assert.That(handler.RequestHeaders[^1].Accept.ToString()).IsEqualTo("text/plain");
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            $"GET {organization}/Web/_apis/build/builds/300/timeline?api-version=7.1",
            $"GET {organization}/Web/_apis/build/builds/300/logs/5?api-version=7.1"
        ]);
    }

    /// <summary>
    /// The size sits in a property the documented schema does not mention, so an artifact whose
    /// resource does not carry it is listed with no size rather than with a zero one.
    /// </summary>
    [Test]
    public async Task ListArtifactsReadsTheUndocumentedSize()
    {
        var handler = Handler()
            .Get(
                $"{organization}/Web/_apis/build/builds/300/artifacts?api-version=7.1",
                """
                {"count":2,"value":[
                  {"id":1,"name":"drop","resource":{"type":"Container","properties":{"artifactsize":"2048"}}},
                  {"id":2,"name":"logs","resource":{"type":"PipelineArtifact","properties":{}}}
                ]}
                """);
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        var artifacts = await ProviderTestHelpers.Provider("azure-devops").ListArtifacts(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await Verify(new
            {
                artifacts,
                handler.Requests
            })
            .Snapshot(
                $$"""
                  {
                    artifacts: [
                      {
                        Id: drop,
                        Name: drop.zip,
                        Bytes: 2048
                      },
                      {
                        Id: logs,
                        Name: logs.zip
                      }
                    ],
                    Requests: [
                      GET {{organization}}/Web/_apis/build/builds/300/artifacts?api-version=7.1
                    ]
                  }
                  """);
    }

    /// <summary>
    /// Through the connection's own address rather than the resource's downloadUrl, which points
    /// at a separate artifacts host where the handler drops the credential.
    /// </summary>
    [Test]
    public async Task DownloadAnArtifactAsAZip()
    {
        var handler = Handler()
            .MapBytes("GET", $"{organization}/Web/_apis/build/builds/300/artifacts?artifactName=drop&$format=zip&api-version=7.1", [.. "PK"u8]);
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        handler.Requests.Clear();
        using var destination = new MemoryStream();
        var written = await ProviderTestHelpers.Provider("azure-devops").DownloadArtifact(
            context,
            builds.Single(_ => _.RunNumber == "20260101.2"),
            new("drop", "drop.zip", 2),
            destination,
            ArtifactPlan.DefaultPerFile,
            Cancel.None);
        await Assert.That(written).IsEqualTo(2);
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {organization}/Web/_apis/build/builds/300/artifacts?artifactName=drop&$format=zip&api-version=7.1");
    }

    [Test]
    public async Task AJobThatFailedWithNoFailedTaskGivesItsOwnLog()
    {
        var handler = Handler()
            .Get(
                $"{organization}/Web/_apis/build/builds/300/timeline?api-version=7.1",
                """{"records":[{"id":"j1","type":"Job","name":"Build","result":"failed","log":{"id":3}}]}""")
            .Get($"{organization}/Web/_apis/build/builds/300/logs/3?api-version=7.1", "The agent was lost\n");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        var builds = await ProviderTestHelpers.DiscoverAndFetch("azure-devops", context);
        var log = await ProviderTestHelpers.Provider("azure-devops").FetchLog(context, builds.Single(_ => _.RunNumber == "20260101.2"), Cancel.None);
        await Assert.That(log).IsEqualTo("==> Build <==\nThe agent was lost");
    }

    [Test]
    public async Task ProjectScopeSkipsProjectDiscovery()
    {
        var handler = new FakeHttpHandler()
            .Get($"{organization}/Web/_apis/pipelines?api-version=7.1", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: [("organization", "contoso"), ("project", "Web")]);
        await ProviderTestHelpers.Provider("azure-devops").DiscoverPipelines(context, Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://dev.azure.com/contoso/Web/_apis/pipelines?api-version=7.1
                ]
                """);
    }

    [Test]
    public async Task PatUsesBasicWithEmptyUser()
    {
        var handler = new FakeHttpHandler().Get($"{organization}/_apis/projects?api-version=7.1&$top=100", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, scope: ("organization", "contoso"));
        await ProviderTestHelpers.Provider("azure-devops").Test(context, Cancel.None);
        var expected = Convert.ToBase64String(":secret"u8.ToArray());
        await Assert.That(handler.RequestHeaders.Single().Authorization!.ToString()).IsEqualTo($"Basic {expected}");
    }

    [Test]
    [Arguments(AuthMethod.Browser)]
    [Arguments(AuthMethod.Device)]
    public async Task ASignInsTokenIsABearerToken(AuthMethod method)
    {
        // The way Microsoft's REST samples send a Microsoft Entra token.
        var handler = new FakeHttpHandler().Get($"{organization}/_apis/projects?api-version=7.1&$top=100", """{"count":0,"value":[]}""");
        var context = ProviderTestHelpers.Context("azure-devops", handler, auth: method, scope: ("organization", "contoso"));
        await ProviderTestHelpers.Provider("azure-devops").Test(context, Cancel.None);
        await Assert.That(handler.RequestHeaders.Single().Authorization!.ToString()).IsEqualTo("Bearer secret");
    }
}
