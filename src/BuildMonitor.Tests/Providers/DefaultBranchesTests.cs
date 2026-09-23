public class DefaultBranchesTests
{
    [Test]
    public async Task TheSettingWhenPullRequestsTargetIt() =>
        await Assert.That(DefaultBranches.Choose(["main", null], ["main", "main"], [])).IsEqualTo("main");

    [Test]
    public async Task PullRequestsOverruleAStaleSetting() =>
        await Assert.That(DefaultBranches.Choose(["master", null], ["main", "main"], ["fix-a"])).IsEqualTo("main");

    [Test]
    public async Task TheSettingWhenItIsBuiltThoughPullRequestsTargetAnother() =>
        await Assert.That(DefaultBranches.Choose(["develop", null], ["main"], ["develop"])).IsEqualTo("develop");

    /// <summary>
    /// One window of backports into a release branch leaves the pipeline on main, which it still
    /// builds.
    /// </summary>
    [Test]
    public async Task TheRememberedBranchWhenItIsBuilt() =>
        await Assert.That(DefaultBranches.Choose([null, "main"], ["release/1.x"], ["main"])).IsEqualTo("main");

    [Test]
    public async Task TheMostTargetedBranchOverAnUnwitnessedMemory() =>
        await Assert.That(DefaultBranches.Choose([null, "master"], ["main", "release/1.x", "main"], [])).IsEqualTo("main");

    [Test]
    public async Task ATieGoesToTheNewestTarget() =>
        await Assert.That(DefaultBranches.Choose([null, null], ["release/1.x", "main"], [])).IsEqualTo("release/1.x");

    [Test]
    public async Task WithNoPullRequestsTheFirstCandidate() =>
        await Assert.That(DefaultBranches.Choose(["master", "main"], [], ["feature"])).IsEqualTo("master");

    [Test]
    public async Task WithNothingToGoByNone() =>
        await Assert.That(DefaultBranches.Choose([null, null], [], ["feature"])).IsNull();
}
