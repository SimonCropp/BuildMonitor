/// <summary>
/// A pipeline deleted since discovery answers its fetch with a 404. Unmapped routes are 404s in the
/// fake handler, so each provider fetches a pipeline nothing answers for, and has to come back with
/// no builds rather than failing the fetch for every other pipeline on the connection.
/// </summary>
public class DeletedPipelineTests
{
    [Test]
    [Arguments("appveyor", null, "simon/gone", null, "https://ci.appveyor.com/project/simon/gone")]
    [Arguments("azure-devops", null, "Gone/1", "Gone", "https://dev.azure.com/org/Gone/_build?definitionId=1")]
    [Arguments("bitbucket", null, "gone", null, "https://bitbucket.org/simon/gone")]
    [Arguments("github", null, "SimonCropp/Gone/1", null, "https://github.com/SimonCropp/Gone/actions")]
    [Arguments("gitlab", null, "1", null, "https://gitlab.com/simon/gone")]
    [Arguments("gocd", "https://gocd.example.com/go", "gone", null, "https://gocd.example.com/go/pipeline/activity/gone")]
    [Arguments("jenkins", "https://jenkins.example.com", "gone", null, "https://jenkins.example.com/job/gone/")]
    [Arguments("teamcity", "https://teamcity.example.com", "Gone_Build", "Gone", "https://teamcity.example.com/buildConfiguration/Gone_Build")]
    [Arguments("travis", null, "simon/gone", null, "https://app.travis-ci.com/github/simon/gone")]
    public async Task HasNoBuilds(string providerId, string? server, string pipelineId, string? group, string url)
    {
        var context = ProviderTestHelpers.Context(providerId, new(), server);
        var repoName = pipelineId.Contains('/') ? pipelineId[..pipelineId.LastIndexOf('/')] : pipelineId;
        if (group is not null)
        {
            repoName = group;
        }

        Pipeline pipeline = new(pipelineId, "Gone", repoName, group, url);
        var builds = await ProviderTestHelpers.Provider(providerId).FetchBuilds(context, [pipeline], 5, Cancel.None);
        await Assert.That(builds).IsEmpty();
    }
}
