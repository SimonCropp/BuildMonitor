public class StringPoolTests
{
    const string runs =
        """
        {
          "workflow_runs": [
            {"id": 1, "status": "completed", "conclusion": "success", "head_branch": "main", "head_commit": {"message": "Bump TUnit"}},
            {"id": 2, "status": "completed", "conclusion": "success", "head_branch": "main", "head_commit": {"message": "Bump TUnit"}}
          ]
        }
        """;

    [Test]
    public async Task ValuesRepeatedAcrossResponsesAreOneInstance()
    {
        var first = JsonSerializer.Deserialize(runs, GitHubContext.Default.GitHubRuns)!.WorkflowRuns;
        var second = JsonSerializer.Deserialize(runs, GitHubContext.Default.GitHubRuns)!.WorkflowRuns;

        await Assert.That(first[0].Status).IsEqualTo("completed");
        await Assert.That(first[1].Status).IsSameReferenceAs(first[0].Status);
        await Assert.That(second[0].Status).IsSameReferenceAs(first[0].Status);
        await Assert.That(second[1].HeadBranch).IsSameReferenceAs(first[0].HeadBranch);
        await Assert.That(second[0].HeadCommit!.Message).IsSameReferenceAs(first[1].HeadCommit!.Message);
    }

    [Test]
    [Arguments("plain")]
    [Arguments("")]
    [Arguments("escaped \"quotes\" and \\ back\nslash")]
    [Arguments("unicode é ✓ 🎉")]
    public async Task ReadsTheTextAsTheReaderWould(string value)
    {
        var json = JsonSerializer.Serialize(value);
        await Assert.That(Read(json)).IsEqualTo(value);
    }

    [Test]
    [Arguments(255)]
    [Arguments(256)]
    [Arguments(257)]
    [Arguments(4096)]
    [Arguments(5000)]
    public async Task ReadsLongTextWhole(int length)
    {
        // Two bytes a char in UTF-8, so the shorter lengths still take the rented buffer.
        var value = new string('é', length / 2) + new string('a', length - length / 2);
        await Assert.That(Read($"\"{value}\"")).IsEqualTo(value);
    }

    [Test]
    public async Task ANumberWhereTextWasExpectedStillFails() =>
        await Assert.That(() => JsonSerializer.Deserialize("""{"workflow_runs":[{"status":5}]}""", GitHubContext.Default.GitHubRuns))
            .Throws<JsonException>();

    [Test]
    public async Task ReadsDictionaryKeys()
    {
        var permissions = JsonSerializer.Deserialize(
            """{"SpacePermissions":{"DeploymentCreate":[{"SpaceId":"Spaces-1"}]}}""",
            OctopusContext.Default.OctopusPermissions)!;
        await Assert.That(permissions.SpacePermissions!["DeploymentCreate"][0].SpaceId).IsEqualTo("Spaces-1");
    }

    [Test]
    public async Task AValueAskedForEachGenerationIsKept()
    {
        var pool = new StringPool(TimeSpan.FromDays(1));
        var first = pool.Get("value");
        pool.Rotate();
        await Assert.That(pool.Get("value")).IsSameReferenceAs(first);
        pool.Rotate();
        await Assert.That(pool.Get("value")).IsSameReferenceAs(first);
    }

    [Test]
    public async Task AValueNotAskedForAWholeGenerationIsDropped()
    {
        var pool = new StringPool(TimeSpan.FromDays(1));
        var first = pool.Get("value");
        pool.Rotate();
        pool.Rotate();
        var second = pool.Get("value");
        await Assert.That(second).IsEqualTo(first);
        await Assert.That(second).IsNotSameReferenceAs(first);
    }

    static string? Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return new StringPool(TimeSpan.FromDays(1)).Read(ref reader);
    }
}
