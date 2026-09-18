public class JenkinsProviderTests
{
    const string server = "https://jenkins.example.com";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{server}/api/json",
                """
                {"jobs":[
                  {"_class":"hudson.model.FreeStyleProject","name":"build-all","displayName":"Build all","url":"https://jenkins.example.com/job/build-all/"},
                  {"_class":"com.cloudbees.hudson.plugins.folder.Folder","name":"team","displayName":"Team","url":"https://jenkins.example.com/job/team/","jobs":[
                    {"_class":"org.jenkinsci.plugins.workflow.multibranch.WorkflowMultiBranchProject","name":"app","displayName":"App","url":"https://jenkins.example.com/job/team/job/app/","jobs":[
                      {"_class":"org.jenkinsci.plugins.workflow.job.WorkflowJob","name":"PR-12","displayName":"PR-12","url":"https://jenkins.example.com/job/team/job/app/job/PR-12/"}
                    ]}
                  ]}
                ]}
                """)
            .Get(
                $"{server}/job/build-all/api/json",
                """
                {"builds":[
                  {"number":501,"url":"https://jenkins.example.com/job/build-all/501/","result":null,"building":true,"timestamp":1767268500000,"duration":0,"actions":[{},{"lastBuiltRevision":{"branch":[{"name":"refs/remotes/origin/main"}]}}]},
                  {"number":500,"url":"https://jenkins.example.com/job/build-all/500/","result":"SUCCESS","building":false,"timestamp":1767264900000,"duration":290000,"actions":[]}
                ],"lastBuild":{"number":501,"estimatedDuration":300000},"inQueue":false,"queueItem":null}
                """)
            .Get(
                $"{server}/job/team/job/app/job/PR-12/api/json",
                """
                {"builds":[
                  {"number":3,"url":"https://jenkins.example.com/job/team/job/app/job/PR-12/3/","result":"FAILURE","building":false,"timestamp":1767261300000,"duration":60000}
                ],"lastBuild":{"number":3,"estimatedDuration":65000},"inQueue":true,"queueItem":{"id":77,"inQueueSince":1767268740000}}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("jenkins", ProviderTestHelpers.Context("jenkins", handler, server, "simon"));
        await Verify(new { builds, handler.Requests });
    }

    /// <summary>
    /// Jenkins names nobody of its own, so a build parameter is the only thing that can. A
    /// parameter of another type arrives as the JSON it is, which read as a name failed the whole
    /// job's fetch.
    /// </summary>
    static async Task<Build> Build(string parameters, IdentityNames? identities = null)
    {
        var handler = new FakeHttpHandler()
            .Get(
                $"{server}/job/build-all/api/json",
                $$"""
                  {"builds":[
                    {"number":500,"url":"https://jenkins.example.com/job/build-all/500/","result":"FAILURE","building":false,"timestamp":1767264900000,"duration":290000,"actions":[{},{"parameters":[{{parameters}}]}]}
                  ],"lastBuild":{"number":500,"estimatedDuration":300000},"inQueue":false,"queueItem":null}
                  """);
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon") with
        {
            Identities = identities ?? new()
        };
        var pipelines = new[] { new Pipeline("build-all", "Build all", "build-all", null, $"{server}/job/build-all/") };
        var builds = await ProviderTestHelpers.Provider("jenkins").FetchBuilds(context, pipelines, 5, Cancel.None);
        return builds.Single();
    }

    [Test]
    public async Task ATriggeredByParameterNamesTheAuthor()
    {
        var build = await Build("""{"name":"TriggeredBy","value":"Ada Lovelace"}""");
        await Assert.That(build.Author).IsEqualTo("Ada Lovelace");
    }

    [Test]
    public async Task ATriggeredByIdIsNamedByWhatAnotherConnectionLearnt()
    {
        const string id = "d1a80549-4d1f-642e-b5d5-9eca49ca5e24";
        var identities = new IdentityNames();
        identities.Add(id, "Simon Cropp");
        var build = await Build($$"""{"name":"TriggeredBy","value":"{{id}}"}""", identities);
        await Assert.That(build.Author).IsEqualTo("Simon Cropp");
    }

    [Test]
    [Arguments("")]
    [Arguments("""{"name":"Other","value":"Ada Lovelace"}""")]
    [Arguments("""{"name":"TriggeredBy","value":true}""")]
    [Arguments("""{"name":"TriggeredBy","value":12}""")]
    [Arguments("""{"name":"TriggeredBy","value":null}""")]
    [Arguments("""{"name":"TriggeredBy","value":"d1a80549-4d1f-642e-b5d5-9eca49ca5e24"}""")]
    public async Task WithoutAUsableParameterTheBuildNamesNobody(string parameters)
    {
        var build = await Build(parameters);
        await Assert.That(build.Author).IsNull();
    }

    [Test]
    public async Task OnlyTheLastBuildCarriesTheEstimate()
    {
        // Asked of every build, estimatedDuration walked up to six earlier builds each, and only a
        // running build, which is the last, reads it.
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("jenkins", ProviderTestHelpers.Context("jenkins", handler, server, "simon"));
        await Assert.That(builds.Single(_ => _.RunNumber == "501").Estimate?.Duration).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(builds.Single(_ => _.RunNumber == "500").Estimate).IsNull();
    }

    [Test]
    public async Task DiscoveryReadsSixLevelsOfFoldersInOneRequest()
    {
        // Each folder deeper than the request reached cost a request of its own.
        var handler = new FakeHttpHandler().Get($"{server}/api/json", """{"jobs":[]}""");
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        await ProviderTestHelpers.Provider("jenkins").DiscoverPipelines(context, Cancel.None);
        await Assert.That(Levels(handler.Requests.Single())).IsEqualTo(6);
    }

    [Test]
    public async Task RecentActivityReadsNextBuildNumbersFromTheTree()
    {
        var handler = new FakeHttpHandler()
            .Get(
                $"{server}/api/json",
                """
                {"jobs":[
                  {"_class":"hudson.model.FreeStyleProject","url":"https://jenkins.example.com/job/build-all/","nextBuildNumber":502,"inQueue":false},
                  {"_class":"com.cloudbees.hudson.plugins.folder.Folder","url":"https://jenkins.example.com/job/team/","jobs":[
                    {"_class":"org.jenkinsci.plugins.workflow.multibranch.WorkflowMultiBranchProject","url":"https://jenkins.example.com/job/team/job/app/","jobs":[
                      {"_class":"org.jenkinsci.plugins.workflow.job.WorkflowJob","url":"https://jenkins.example.com/job/team/job/app/job/PR-12/","nextBuildNumber":4,"inQueue":true}
                    ]}
                  ]}
                ]}
                """);
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        var activity = await ProviderTestHelpers.Provider("jenkins").RecentActivity(context, [], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(activity!.Count).IsEqualTo(2);
        await Assert.That(activity["https://jenkins.example.com/job/build-all/"]).IsEqualTo("502|False");
        await Assert.That(activity["https://jenkins.example.com/job/team/job/app/job/PR-12/"]).IsEqualTo("4|True");
        await Assert.That(handler.Requests.Single()).StartsWith($"GET {server}/api/json?tree=jobs[url,_class,nextBuildNumber,inQueue,jobs[");
    }

    [Test]
    public async Task RecentActivityReachesTheDeepestJobDiscovered()
    {
        // Discovery reaches a deeper folder with a request of its own. A job below the probe's tree
        // had no token, so it waited for its schedule whatever happened to it.
        const string deep = "https://jenkins.example.com/job/a/job/b/job/c/job/d/job/e/job/f/job/g/job/h/";
        var handler = new FakeHttpHandler().Get($"{server}/api/json", """{"jobs":[]}""");
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        var group = new PollGroup(deep, [new(deep, "a / b / c / d / e / f / g / h", "a / b / c / d / e / f / g / h", null, deep)]);
        await ProviderTestHelpers.Provider("jenkins").RecentActivity(context, [group], ImmutableDictionary<string, string>.Empty, Cancel.None);
        await Assert.That(Levels(handler.Requests.Single())).IsEqualTo(8);
    }

    static int Levels(string request) =>
        request.Split("jobs[").Length - 1;

    [Test]
    public async Task RetryFallsBackToParameters()
    {
        var handler = Handler()
            .Get($"{server}/crumbIssuer/api/json", """{"crumbRequestField":"Jenkins-Crumb","crumb":"c1"}""")
            .Map("POST", $"{server}/job/build-all/build", "", HttpStatusCode.BadRequest)
            .Map("POST", $"{server}/job/build-all/buildWithParameters", "", HttpStatusCode.Created);
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("jenkins", context);
        handler.Requests.Clear();
        await ProviderTestHelpers.Provider("jenkins").Retry(context, builds.Single(_ => _.RunNumber == "500"), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://jenkins.example.com/crumbIssuer/api/json,
                  POST https://jenkins.example.com/job/build-all/build
                  Jenkins-Crumb: c1
                  Referer: https://jenkins.example.com/whoAmI/api/json,
                  POST https://jenkins.example.com/job/build-all/buildWithParameters
                  Jenkins-Crumb: c1
                  Referer: https://jenkins.example.com/whoAmI/api/json
                ]
                """);
    }

    [Test]
    public async Task CancelRunningAndQueued()
    {
        var handler = Handler()
            .Map("GET", $"{server}/crumbIssuer/api/json", "", HttpStatusCode.NotFound)
            .Map("POST", $"{server}/job/build-all/501/stop", "", HttpStatusCode.Found)
            .Map("POST", $"{server}/queue/cancelItem?id=77", "", HttpStatusCode.NoContent);
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("jenkins", context);
        handler.Requests.Clear();
        var provider = ProviderTestHelpers.Provider("jenkins");
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "501"), Cancel.None);
        await provider.Cancel(context, builds.Single(_ => _ is {RunNumber: "", PipelineName: "Team / App / PR-12"}), Cancel.None);
        await Verify(handler.Requests)
            .Snapshot(
                """
                [
                  GET https://jenkins.example.com/crumbIssuer/api/json,
                  POST https://jenkins.example.com/job/build-all/501/stop
                  Referer: https://jenkins.example.com/whoAmI/api/json,
                  GET https://jenkins.example.com/crumbIssuer/api/json,
                  POST https://jenkins.example.com/queue/cancelItem?id=77
                  Referer: https://jenkins.example.com/whoAmI/api/json
                ]
                """);
    }

    [Test]
    public async Task CancelSurvivesTheRedirectAStopAnswersWith()
    {
        // Jenkins answers a stop with a redirect, which a real handler follows without the
        // credential. On a server that anonymous users may not read, that page refused the
        // request, so the cancel reported a 403 for a build that had stopped.
        await using var jenkins = new SecuredJenkins();
        using var handler = new SocketsHttpHandler();
        var connection = new Connection
        {
            Id = "jenkins",
            ProviderId = "jenkins",
            Name = "Jenkins",
            Server = jenkins.Server,
            User = "simon"
        };
        var context = Providers.Context(connection, "secret", handler);
        var job = $"{jenkins.Server}/job/app/";
        var build = Fixtures.Build("jenkins", job, "App", "App", null, "7", BuildStatus.Running) with
        {
            ProviderRef = $"{job}|7"
        };
        await ProviderTestHelpers.Provider("jenkins").Cancel(context, build, Cancel.None);
        await Verify(jenkins.Requests)
            .Snapshot(
                """
                [
                  GET /crumbIssuer/api/json (signed in),
                  POST /job/app/7/stop (signed in),
                  GET /whoAmI/api/json
                ]
                """);
    }

    [Test]
    public async Task FetchLogReadsTheConsole()
    {
        var handler = Handler()
            .Get($"{server}/job/team/job/app/job/PR-12/3/consoleText", "ERROR: script returned exit code 1\n");
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        var builds = await ProviderTestHelpers.DiscoverAndFetch("jenkins", context);
        handler.Requests.Clear();
        var log = await ProviderTestHelpers.Provider("jenkins").FetchLog(context, builds.Single(_ => _.RunNumber == "3"), Cancel.None);
        await Assert.That(log).IsEqualTo("ERROR: script returned exit code 1\n");
        await Assert.That(handler.Requests.Single()).IsEqualTo($"GET {server}/job/team/job/app/job/PR-12/3/consoleText");
    }

    [Test]
    public async Task UsesBasicAuthWithTheUser()
    {
        var handler = new FakeHttpHandler().Get($"{server}/me/api/json?tree=fullName,id", """{"id":"simon","fullName":"Simon"}""");
        var context = ProviderTestHelpers.Context("jenkins", handler, server, "simon");
        var result = await ProviderTestHelpers.Provider("jenkins").Test(context, Cancel.None);
        await Assert.That(result.Message).IsEqualTo("Signed in as Simon");
        var expected = Convert.ToBase64String("simon:secret"u8.ToArray());
        await Assert.That(handler.RequestHeaders.Single().Authorization!.ToString()).IsEqualTo($"Basic {expected}");
    }
}
