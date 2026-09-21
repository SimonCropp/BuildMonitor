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
        var build = Build(repo, Enum.Parse<BuildStatus>(status));
        await Assert.That(GroupKey.IdOf(build, [])).IsEqualTo(expected);
        await Assert.That(GroupKey.Of(build, [])?.Id).IsEqualTo(expected);
    }

    [Test]
    // The project column's name, so the owner is no part of the match.
    [Arguments("owner/TheProjectApi", "TheProject")]
    [Arguments("owner/theprojectui", "TheProject")]
    [Arguments("owner/THEPROJECT", "TheProject")]
    // The longest match, for someone who wants a narrower group inside a wider one.
    [Arguments("owner/TheProjectManager.Messages", "TheProjectManager")]
    // No prefix matches, so the repository name still keys the group.
    [Arguments("owner/DiffEngine", "DiffEngine")]
    public async Task APrefixKeysTheGroupAheadOfTheProject(string repo, string expected)
    {
        ImmutableArray<string> prefixes = ["TheProject", "TheProjectManager"];
        var build = Build(repo, BuildStatus.Succeeded);
        await Assert.That(GroupKey.Of(build, prefixes)!.Project).IsEqualTo(expected);
        await Assert.That(GroupKey.IdOf(build, prefixes)).IsEqualTo(expected.ToLowerInvariant());
    }

    /// <summary>
    /// The group the service files the pipeline under, where nothing typed matches it: an Octopus
    /// project group names a family of projects nobody had to describe.
    /// </summary>
    [Test]
    public async Task TheServicesOwnGroupKeysAheadOfTheProject()
    {
        var build = Build("Deploy Api", BuildStatus.Succeeded) with { ProjectGroup = "Storefront" };
        await Assert.That(GroupKey.Of(build, [])!.Project).IsEqualTo("Storefront");
        await Assert.That(GroupKey.IdOf(build, [])).IsEqualTo("storefront");
    }

    /// <summary>
    /// A typed prefix is the one of the two someone asked for, so it wins.
    /// </summary>
    [Test]
    public async Task APrefixBeatsTheServicesOwnGroup()
    {
        var build = Build("TheProjectApi", BuildStatus.Succeeded) with { ProjectGroup = "Storefront" };
        await Assert.That(GroupKey.Of(build, ["TheProject"])!.Project).IsEqualTo("TheProject");
        await Assert.That(GroupKey.IdOf(build, ["TheProject"])).IsEqualTo("theproject");
    }

    /// <summary>
    /// A prefix is no reason to group a build that is not passing: the row that says what broke
    /// would be the one the group hid.
    /// </summary>
    [Test]
    public async Task APrefixDoesNotGroupAFailure()
    {
        var build = Build("owner/TheProjectApi", BuildStatus.Failed);
        await Assert.That(GroupKey.IdOf(build, ["TheProject"])).IsNull();
        await Assert.That(GroupKey.Of(build, ["TheProject"])).IsNull();
    }

    static Build Build(string repo, BuildStatus status) =>
        Fixtures.Build(Fixtures.GitHub.Id, "test.yml", "test.yml", repo, "main", "1", status);
}
