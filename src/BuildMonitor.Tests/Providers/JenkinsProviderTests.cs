public class JenkinsProviderTests
{
    const string server = "https://jenkins.example.com";

    static FakeHttpHandler Handler() =>
        new FakeHttpHandler()
            .Get(
                $"{server}/api/json",
                """
                {"jobs":[
                  {"_class":"hudson.model.FreeStyleProject","name":"build-all","displayName":"Build all","url":"https://jenkins.example.com/job/build-all/","color":"blue_anime"},
                  {"_class":"com.cloudbees.hudson.plugins.folder.Folder","name":"team","displayName":"Team","url":"https://jenkins.example.com/job/team/","jobs":[
                    {"_class":"org.jenkinsci.plugins.workflow.multibranch.WorkflowMultiBranchProject","name":"app","displayName":"App","url":"https://jenkins.example.com/job/team/job/app/","jobs":[
                      {"_class":"org.jenkinsci.plugins.workflow.job.WorkflowJob","name":"PR-12","displayName":"PR-12","url":"https://jenkins.example.com/job/team/job/app/job/PR-12/","color":"red"}
                    ]}
                  ]}
                ]}
                """)
            .Get(
                $"{server}/job/build-all/api/json",
                """
                {"builds":[
                  {"number":501,"url":"https://jenkins.example.com/job/build-all/501/","result":null,"building":true,"timestamp":1767268500000,"duration":0,"estimatedDuration":300000,"actions":[{},{"lastBuiltRevision":{"branch":[{"name":"refs/remotes/origin/main"}]}}]},
                  {"number":500,"url":"https://jenkins.example.com/job/build-all/500/","result":"SUCCESS","building":false,"timestamp":1767264900000,"duration":290000,"estimatedDuration":300000,"actions":[]}
                ],"inQueue":false,"queueItem":null}
                """)
            .Get(
                $"{server}/job/team/job/app/job/PR-12/api/json",
                """
                {"builds":[
                  {"number":3,"url":"https://jenkins.example.com/job/team/job/app/job/PR-12/3/","result":"FAILURE","building":false,"timestamp":1767261300000,"duration":60000,"estimatedDuration":65000,"actions":[]}
                ],"inQueue":true,"queueItem":{"id":77,"inQueueSince":1767268740000}}
                """);

    [Test]
    public async Task DiscoverAndFetch()
    {
        var handler = Handler();
        var builds = await ProviderTestHelpers.DiscoverAndFetch("jenkins", ProviderTestHelpers.Context("jenkins", handler, server, "simon"));
        await Verify(new { builds, handler.Requests });
    }

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
        await Verify(handler.Requests);
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
        await provider.Cancel(context, builds.Single(_ => _.RunNumber == "" && _.PipelineName == "Team / App / PR-12"), Cancel.None);
        await Verify(handler.Requests);
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
