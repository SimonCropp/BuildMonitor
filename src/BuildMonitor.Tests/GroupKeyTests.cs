public class GroupKeyTests
{
    [Test]
    [Arguments("Verify")]
    [Arguments("VERIFY")]
    [Arguments("verify")]
    [Arguments("ÄÖÜ.Net")]
    [Arguments("İstanbul")]
    [Arguments("")]
    public async Task TheIdLowerCasesTheProject(string project)
    {
        await Assert.That(new GroupKey(project, true).Id).IsEqualTo($"failed/{project.ToLowerInvariant()}");
        await Assert.That(new GroupKey(project, false).Id).IsEqualTo($"passed/{project.ToLowerInvariant()}");
    }

    [Test]
    public async Task AProjectTooLongForTheStackIsLowerCasedToo()
    {
        var project = new string('A', 300);
        await Assert.That(new GroupKey(project, false).Id).IsEqualTo($"passed/{new string('a', 300)}");
    }

    [Test]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Failed), "failed/verify")]
    [Arguments("VerifyTests/Verify", nameof(BuildStatus.Succeeded), "passed/verify")]
    [Arguments("Verify", nameof(BuildStatus.Succeeded), "passed/verify")]
    [Arguments("owner/", nameof(BuildStatus.Failed), "failed/")]
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
