public class RepoHostsTests
{
    [Test]
    [Arguments("https://github.com/SimonCropp/Verify", "host-github")]
    // An enterprise server, which is the case a mark taken from the provider would get wrong.
    [Arguments("https://github.example.com/team/app", "host-github")]
    [Arguments("https://gitlab.com/group/app", "host-gitlab")]
    [Arguments("https://gitlab.example.com/group/app", "host-gitlab")]
    [Arguments("https://bitbucket.org/team/app", "host-bitbucket")]
    [Arguments("https://dev.azure.com/org/project/_git/app", "host-azure-devops")]
    [Arguments("https://org.visualstudio.com/project/_git/app", "host-azure-devops")]
    public async Task NamesTheMarkOfTheHost(string url, string expected)
    {
        await Assert.That(RepoHosts.MarkOf(url)).IsEqualTo(expected);
        // A head hands its pictures over before the first frame, from this list, so a mark left
        // out of it is a row that draws nothing where its mark should be.
        await Assert.That(RepoHosts.All).Contains(expected);
    }

    [Test]
    // A host nothing here has a mark for, which is most self hosted Git.
    [Arguments("https://git.example.com/team/app.git")]
    [Arguments("https://example.com/github/app")]
    [Arguments("not a url")]
    [Arguments(null)]
    public async Task LeavesAHostItHasNoMarkForBare(string? url) =>
        await Assert.That(RepoHosts.MarkOf(url)).IsEmpty();
}
