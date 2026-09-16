public class AuthorNamesTests
{
    [Test]
    public async Task FirstNamesUnlessShared()
    {
        var names = AuthorNames.Of(["Simon Cropp", "Simon Smith", "Jane Doe", "jane doe", " octocat ", null, ""]);
        await Assert.That(names.Count).IsEqualTo(4);
        await Assert.That(names["Simon Cropp"]).IsEqualTo("Simon Cropp");
        await Assert.That(names["Simon Smith"]).IsEqualTo("Simon Smith");
        await Assert.That(names["Jane Doe"]).IsEqualTo("Jane");
        await Assert.That(names["octocat"]).IsEqualTo("octocat");
    }

    [Test]
    public async Task BotsDropTheSuffixUnlessShared()
    {
        var names = AuthorNames.Of(["dependabot[bot]", "github-actions[bot] <bot@users.noreply.github.com>", "renovate[bot]", "Renovate Bot"]);
        await Assert.That(names["dependabot[bot]"]).IsEqualTo("dependabot");
        await Assert.That(names["github-actions[bot] <bot@users.noreply.github.com>"]).IsEqualTo("github-actions");
        await Assert.That(names["renovate[bot]"]).IsEqualTo("renovate[bot]");
        await Assert.That(names["Renovate Bot"]).IsEqualTo("Renovate Bot");
    }
}
