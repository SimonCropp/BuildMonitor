public class PullRequestBranchesTests
{
    [Test]
    [Arguments("feature", null, "feature")]
    [Arguments("main", "someone", "someone:main")]
    public async Task Head(string branch, string? forkOwner, string expected) =>
        await Assert.That(PullRequestBranches.Head(branch, forkOwner)).IsEqualTo(expected);

    [Test]
    public async Task UnnamedIsThePullRequestsOwnRef() =>
        await Assert.That(PullRequestBranches.Unnamed("12")).IsEqualTo("pull/12");

    [Test]
    [Arguments("someone/DiffEngine", "VerifyTests/DiffEngine", "someone")]
    [Arguments("VerifyTests/DiffEngine", "VerifyTests/DiffEngine", null)]
    [Arguments("verifytests/diffengine", "VerifyTests/DiffEngine", null)]
    [Arguments(null, "VerifyTests/DiffEngine", null)]
    [Arguments("DiffEngine", "VerifyTests/DiffEngine", null)]
    [Arguments("someone/DiffEngine", "DiffEngine", null)]
    public async Task ForkOwner(string? headRepository, string repository, string? expected) =>
        await Assert.That(PullRequestBranches.ForkOwner(headRepository, repository)).IsEqualTo(expected);
}
