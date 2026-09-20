public class GroupKeyTests
{
    [Test]
    [Arguments("Verify")]
    [Arguments("VERIFY")]
    [Arguments("verify")]
    [Arguments("ÄÖÜ.Net")]
    [Arguments("İstanbul")]
    [Arguments("")]
    public async Task TheIdLowerCasesTheProject(string project) =>
        await Assert.That(new GroupKey(project).Id).IsEqualTo(project.ToLowerInvariant());

    [Test]
    public async Task AProjectTooLongForTheStackIsLowerCasedToo()
    {
        var project = new string('A', 300);
        await Assert.That(new GroupKey(project).Id).IsEqualTo(new('a', 300));
    }

    [Test]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Succeeded), "verify")]
    [Arguments("Verify", nameof(BuildStatus.Succeeded), "verify")]
    [Arguments("owner/", nameof(BuildStatus.Succeeded), "")]
    // Only passes group: a failure keeps the row of its own that says which pipeline broke.
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Failed), null)]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Running), null)]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Queued), null)]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Cancelled), null)]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Unknown), null)]
    public async Task ABuildsIdIsItsKeysId(string repo, string status, string? expected)
    {
        var build = Fixtures.Build(Fixtures.GitHub.Id, "test.yml", "test.yml", repo, "main", "1", Enum.Parse<BuildStatus>(status));
        await Assert.That(GroupKey.IdOf(build)).IsEqualTo(expected);
        await Assert.That(GroupKey.Of(build)?.Id).IsEqualTo(expected);
    }
}
