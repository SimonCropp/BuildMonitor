/// <summary>
/// Which connection a failed branch is asked of. Its credential goes with the question, so a
/// repository is only ever asked of the connection whose own address is the repository's host.
/// </summary>
public class BranchHostsTests
{
    [Test]
    [Arguments("github", null, "https://github.com/VerifyTests/Verify", true)]
    [Arguments("github", null, "https://GitHub.com/VerifyTests/Verify", true)]
    [Arguments("github", null, "https://gitlab.com/group/project", false)]
    [Arguments("github", "https://github.example.com", "https://github.example.com/team/app", true)]
    [Arguments("github", "https://github.example.com", "https://github.com/VerifyTests/Verify", false)]
    // A host that only has the service's name in it is someone else's.
    [Arguments("github", null, "https://notgithub.com/VerifyTests/Verify", false)]
    [Arguments("github", null, "https://github.com.example.net/VerifyTests/Verify", false)]
    // A service that only builds holds no repositories to ask about.
    [Arguments("appveyor", null, "https://github.com/VerifyTests/Verify", false)]
    [Arguments("travis", null, "https://github.com/VerifyTests/Verify", false)]
    public async Task Answers(string provider, string? server, string repository, bool answers)
    {
        var connection = new Connection
        {
            Id = "x",
            ProviderId = provider,
            Name = "x",
            Server = server
        };
        await Assert.That(BranchHosts.Answers(connection, repository)).IsEqualTo(answers);
    }

    [Test]
    public async Task ABuildIsAskableWhenAnyConnectionAnswersForItsRepository()
    {
        var askable = BranchHosts.Askable([Fixtures.Jenkins, Fixtures.GitHub]);
        var build = Fixtures.GitHubBuilds()[1];
        await Assert.That(askable(build)).IsTrue();
        await Assert.That(askable(build with { RepoUrl = "https://gitlab.com/group/project" })).IsFalse();
        await Assert.That(askable(build with { RepoUrl = null })).IsFalse();
        await Assert.That(BranchHosts.Askable([Fixtures.Jenkins])(build)).IsFalse();
    }
}
