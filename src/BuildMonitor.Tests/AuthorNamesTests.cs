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
}
