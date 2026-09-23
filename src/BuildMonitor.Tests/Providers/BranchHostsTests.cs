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
    [Arguments("gitlab", null, "https://gitlab.com/group/sub/project", true)]
    [Arguments("gitlab", "https://example.com/gitlab", "https://example.com/gitlab/group/project", true)]
    // A path that only starts with the server's is another server's.
    [Arguments("gitlab", "https://example.com/gitlab", "https://example.com/gitlabs/group/project", false)]
    [Arguments("gitlab", null, "https://github.com/VerifyTests/Verify", false)]
    [Arguments("bitbucket", null, "https://bitbucket.org/verify/diffengine", true)]
    [Arguments("bitbucket", null, "https://github.com/VerifyTests/Verify", false)]
    [Arguments("azure-devops", null, "https://dev.azure.com/contoso/Verify/_git/DiffEngine", true)]
    // Another organization's repository is not this connection's, though the host is the same.
    [Arguments("azure-devops", null, "https://dev.azure.com/fabrikam/Verify/_git/DiffEngine", false)]
    [Arguments("azure-devops", null, "https://github.com/VerifyTests/Verify", false)]
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
            Server = server,
            Scope = ImmutableDictionary<string, string>.Empty.Add("organization", "contoso")
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
