public class FiltersTests
{
    [Test]
    [Arguments(FilterKind.Exact, "Nightly", "Nightly", true)]
    [Arguments(FilterKind.Exact, "nightly", "Nightly", true)]
    [Arguments(FilterKind.Exact, "Night", "Nightly", false)]
    [Arguments(FilterKind.Prefix, "Night", "Nightly", true)]
    [Arguments(FilterKind.Prefix, "ly", "Nightly", false)]
    [Arguments(FilterKind.Suffix, "ly", "Nightly", true)]
    [Arguments(FilterKind.Suffix, "Night", "Nightly", false)]
    [Arguments(FilterKind.Contains, "ght", "Nightly", true)]
    [Arguments(FilterKind.Contains, "xyz", "Nightly", false)]
    public async Task Matches(FilterKind kind, string text, string value, bool expected) =>
        await Assert.That(Filters.Matches(new(kind, FilterTarget.Pipeline, text), value)).IsEqualTo(expected);

    [Test]
    public async Task TargetsApplyToTheirField()
    {
        var build = Fixtures.GitHubBuilds()[1];
        await Assert.That(Filters.Excludes([new(FilterKind.Exact, FilterTarget.Pipeline, "test.yml")], build)).IsTrue();
        await Assert.That(Filters.Excludes([new(FilterKind.Prefix, FilterTarget.Repo, "VerifyTests/")], build)).IsTrue();
        await Assert.That(Filters.Excludes([new(FilterKind.Prefix, FilterTarget.Branch, "feature/")], build)).IsTrue();
        await Assert.That(Filters.Excludes([new(FilterKind.Prefix, FilterTarget.Branch, "main")], build)).IsFalse();
    }

    [Test]
    public async Task PipelineFiltersRunBeforeFetch()
    {
        var pipeline = new Pipeline("1", "Nightly", "repo", null, "https://example");
        await Assert.That(Filters.ExcludesPipeline([new(FilterKind.Exact, FilterTarget.Pipeline, "Nightly")], pipeline)).IsTrue();
        await Assert.That(Filters.ExcludesPipeline([new(FilterKind.Exact, FilterTarget.Branch, "main")], pipeline)).IsFalse();
    }
}
