public class PollGroupTests
{
    static readonly Pipeline[] pipelines =
    [
        new("VerifyTests/Verify/2", "docs", "VerifyTests/Verify", "VerifyTests/Verify", "https://github.com/VerifyTests/Verify"),
        new("VerifyTests/DiffEngine/1", "ci", "VerifyTests/DiffEngine", "VerifyTests/DiffEngine", "https://github.com/VerifyTests/DiffEngine"),
        new("VerifyTests/Verify/1", "ci", "VerifyTests/Verify", "VerifyTests/Verify", "https://github.com/VerifyTests/Verify")
    ];

    [Test]
    [Arguments("Repository", "VerifyTests/Verify|VerifyTests/DiffEngine")]
    [Arguments("Pipeline", "VerifyTests/Verify/2|VerifyTests/DiffEngine/1|VerifyTests/Verify/1")]
    [Arguments("Connection", "")]
    [Arguments("Group", "VerifyTests/Verify|VerifyTests/DiffEngine")]
    public async Task GroupsFollowTheFetchUnitInDiscoveryOrder(string unit, string keys) =>
        await Assert.That(string.Join('|', PollGroup.Of(Enum.Parse<FetchUnit>(unit), pipelines).Select(_ => _.Key))).IsEqualTo(keys);

    [Test]
    public async Task ARepositoryGroupHoldsEveryWorkflowInIt() =>
        await Assert.That(PollGroup.Of(FetchUnit.Repository, pipelines)[0].Pipelines.Select(_ => _.Id))
            .IsEquivalentTo(["VerifyTests/Verify/2", "VerifyTests/Verify/1"]);
}
