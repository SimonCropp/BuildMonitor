public class LocalReposTests
{
    [Test]
    public async Task FindsCheckoutsAtOneAndTwoLevelsButNoDeeper()
    {
        var root = TempDirectory();
        try
        {
            Repo(root, "DiffEngine");
            Repo(root, "org/Verify");
            Repo(root, "org/nested/TooDeep");
            Directory.CreateDirectory(Path.Combine(root, "notes"));

            var found = LocalRepos.Scan(root);

            await Assert.That(found.Select(_ => _.Name).Order()).IsEquivalentTo(["DiffEngine", "Verify"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A submodule or a vendored dependency is another .git inside a checkout. Descending into one
    /// would list it as a repository of its own and let it win the row of the one holding it.
    /// </summary>
    [Test]
    public async Task DoesNotDescendIntoACheckout()
    {
        var root = TempDirectory();
        try
        {
            Repo(root, "Outer");
            Repo(root, "Outer/vendored");

            var found = LocalRepos.Scan(root);

            await Assert.That(found.Select(_ => _.Name)).IsEquivalentTo(["Outer"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task ReadsTheOriginOfEachCheckout()
    {
        var root = TempDirectory();
        try
        {
            Repo(root, "DiffEngine", "https://github.com/VerifyTests/DiffEngine.git");
            Repo(root, "build-all");

            var found = LocalRepos.Scan(root).OrderBy(_ => _.Name).ToList();

            await Assert.That(found[0].Remote).IsNull();
            await Assert.That(found[1].Remote).IsEqualTo("VerifyTests/DiffEngine");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task AMissingRootScansToNothing()
    {
        await Assert.That(LocalRepos.Scan(Path.Combine(Path.GetTempPath(), $"BuildMonitorGone_{Guid.NewGuid():N}"))).IsEmpty();
        await Assert.That(LocalRepos.Scan("")).IsEmpty();
        await Assert.That(LocalRepos.Scan(null)).IsEmpty();
    }

    [Test]
    public async Task AWholeRepositoryNameBeatsAFolderOfTheSameName()
    {
        var index = LocalRepos.Index(
        [
            new("/code/theirs/DiffEngine", "DiffEngine", null),
            new("/code/ours/DiffEngine", "DiffEngine", "VerifyTests/DiffEngine")
        ]);

        await Assert.That(LocalRepos.Find(index, "VerifyTests/DiffEngine")).IsEqualTo("/code/ours/DiffEngine");
    }

    /// <summary>
    /// TeamCity, Octopus, GoCd and Jenkins report a project rather than a slug, so the folder's own
    /// name is the only thing left to match on.
    /// </summary>
    [Test]
    [Arguments("build-all", "/code/build-all")]
    [Arguments("VerifyTests/DiffEngine", "/code/DiffEngine")]
    [Arguments("SomeoneElse/DiffEngine", "/code/DiffEngine")]
    [Arguments("VerifyTests/Unwatched", null)]
    [Arguments("", null)]
    public async Task MatchesARepositoryName(string repoName, string? expected)
    {
        var index = LocalRepos.Index(
        [
            new("/code/DiffEngine", "DiffEngine", "VerifyTests/DiffEngine"),
            new("/code/build-all", "build-all", null)
        ]);

        await Assert.That(LocalRepos.Find(index, repoName)).IsEqualTo(expected);
    }

    [Test]
    public async Task NothingMatchesAnEmptyIndex() =>
        await Assert.That(LocalRepos.Find(ImmutableDictionary<string, string>.Empty, "VerifyTests/DiffEngine")).IsNull();

    /// <summary>
    /// A checkout at <paramref name="relative"/> below the root, whose separators are slashes
    /// whichever platform the test runs on.
    /// </summary>
    static void Repo(string root, string relative, string? origin = null)
    {
        var directory = Path.Combine([root, ..relative.Split('/')]);
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        if (origin is not null)
        {
            File.WriteAllText(
                Path.Combine(directory, ".git", "config"),
                $"[core]\n\tbare = false\n[remote \"origin\"]\n\turl = {origin}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");
        }
    }

    static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BuildMonitorRepos_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
