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

    /// <summary>
    /// The one app on screen is just a robot: which app it was says nothing the mark does not, and
    /// a person with a name like one is still a person, since only the suffix marks an app.
    /// </summary>
    [Test]
    public async Task ABotIsARobot()
    {
        var names = AuthorNames.Of(["dependabot[bot]", "Simon Cropp", "Renovate Bot"]);
        await Assert.That(names["dependabot[bot]"]).IsEqualTo("🤖");
        await Assert.That(names["Simon Cropp"]).IsEqualTo("Simon");
        await Assert.That(names["Renovate Bot"]).IsEqualTo("Renovate");
    }

    /// <summary>
    /// Two apps at once, where the robot alone would point at the wrong one, on the same rule as
    /// two people sharing a first name.
    /// </summary>
    [Test]
    public async Task BotsKeepTheirNameWhereTwoAreShown()
    {
        var names = AuthorNames.Of(["dependabot[bot]", "github-actions[bot] <bot@users.noreply.github.com>"]);
        await Assert.That(names["dependabot[bot]"]).IsEqualTo("🤖 dependabot");
        await Assert.That(names["github-actions[bot] <bot@users.noreply.github.com>"]).IsEqualTo("🤖 github-actions");
    }
}
