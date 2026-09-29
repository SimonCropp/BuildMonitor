public class BuildKeyTests
{
    static Build build = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "feature/inline", "77", BuildStatus.Failed);

    [Test]
    [Arguments("gh/Verify/test.yml/feature/inline")]
    [Arguments("gh/Verify/test.yml/feature/inlinE")]
    [Arguments("gh/Verify/test.yml/feature/inlin")]
    [Arguments("gh/Verify/test.yml/feature/inline/")]
    [Arguments("gh/Verify/test.yml/feature")]
    [Arguments("gh/Verify/test.yml")]
    [Arguments("gh/Verify/test.ymlXfeature/inline")]
    [Arguments("gh/Verify/test.ym/lfeature/inline")]
    [Arguments("ghXVerify/test.yml/feature/inline")]
    [Arguments("gx/Verify/test.yml/feature/inline")]
    [Arguments("gh/Verify/test.yXl/feature/inline")]
    [Arguments("")]
    [Arguments(null)]
    public async Task HasKeyMatchesOnlyTheKey(string? key) =>
        await Assert.That(build.HasKey(key)).IsEqualTo(key == build.Key);

    [Test]
    [Arguments("gh/Verify/test.yml/")]
    [Arguments("gh/Verify/test.yml//")]
    [Arguments("gh/Verify/test.yml")]
    public async Task AMissingBranchKeysAsAnEmptyOne(string key)
    {
        var branchless = build
            with
            {
                Branch = null
            };
        await Assert.That(branchless.HasKey(key)).IsEqualTo(key == branchless.Key);
    }

    [Test]
    [Arguments("gh/Verify/test.yml")]
    [Arguments("gh/Verify/test.ym")]
    [Arguments("gh/Verify/test.ymX")]
    [Arguments("ghXVerify/test.yml")]
    [Arguments("gh/Verify/test.yml/")]
    [Arguments("gh/Verify/test.yml/feature/inline")]
    [Arguments("")]
    public async Task HasPipelineKeyMatchesOnlyThePipelineKey(string key) =>
        await Assert.That(build.HasPipelineKey(key)).IsEqualTo(key == build.PipelineKey);

    [Test]
    public async Task SameKeyAndSamePipelineCompareTheParts()
    {
        await Assert.That(
            build.SameKey(
                build with
                {
                    RunNumber = "78"
                }))
            .IsTrue();
        await Assert.That(
                build.SameKey(
                    build with
                    {
                        Branch = "main"
                    }))
            .IsFalse();
        await Assert.That(
                (
                    build with
                    {
                        Branch = null
                    })
                .SameKey(
                    build with
                    {
                        Branch = ""
                    }))
            .IsTrue();
        await Assert.That(build.SamePipeline(
                build with
                {
                    Branch = "main"
                }))
            .IsTrue();
        await Assert.That(build.SamePipeline(
            build with
            {
                PipelineId = "Verify/docs.yml"
            }))
            .IsFalse();
        await Assert.That(build.SamePipeline(
                build with
                {
                    ConnectionId = "other"
                }))
            .IsFalse();
    }

    [Test]
    [Arguments("gh", "Verify/test.yml", "feature/inline")]
    [Arguments("gh", "Verify/test.yml", "feature/inlinE")]
    [Arguments("gh", "Verify/test.yml", "main")]
    [Arguments("gh", "Verify/test.yml", "")]
    [Arguments("gh", "Verify/test.yml", null)]
    [Arguments("gh", "Verify/docs.yml", "feature/inline")]
    [Arguments("gx", "Verify/test.yml", "feature/inline")]
    // A shorter part, whose separator meets the next character of the longer one: before a letter,
    // after a hyphen, and against a separator of the longer one's own.
    [Arguments("gh", "Verify/test", "feature/inline")]
    [Arguments("gh", "Verify/test.yml-2", "feature/inline")]
    [Arguments("gh", "Verify/test.yml/feature", "inline")]
    [Arguments("gh", "Verify", "test.yml/feature/inline")]
    [Arguments("g", "h/Verify/test.yml", "feature/inline")]
    [Arguments("gh-2", "Verify/test.yml", "feature/inline")]
    [Arguments("gh/Verify", "test.yml", "feature/inline")]
    public async Task CompareKeyOrdersAsTheKeysDo(string connection, string pipeline, string? branch)
    {
        var other = build with
        {
            ConnectionId = connection,
            PipelineId = pipeline,
            Branch = branch
        };
        await Assert.That(Math.Sign(build.CompareKey(other))).IsEqualTo(Math.Sign(string.CompareOrdinal(build.Key, other.Key)));
        await Assert.That(Math.Sign(other.CompareKey(build))).IsEqualTo(Math.Sign(string.CompareOrdinal(other.Key, build.Key)));
        await Assert.That(Math.Sign(Build.ByKey.Compare(build, other))).IsEqualTo(Math.Sign(string.CompareOrdinal(build.Key, other.Key)));
    }
}
