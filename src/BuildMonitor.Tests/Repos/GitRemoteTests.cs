public class GitRemoteTests
{
    [Test]
    [Arguments("git@github.com:VerifyTests/DiffEngine.git", "VerifyTests/DiffEngine")]
    [Arguments("git@github.com:VerifyTests/DiffEngine", "VerifyTests/DiffEngine")]
    [Arguments("https://github.com/VerifyTests/DiffEngine.git", "VerifyTests/DiffEngine")]
    [Arguments("https://github.com/VerifyTests/DiffEngine", "VerifyTests/DiffEngine")]
    [Arguments("https://simon@dev.azure.com/simon/Project/_git/Repo", "simon/Project/_git/Repo")]
    // GitLab reports a namespaced path, which is this whole tail and not just the last segment.
    [Arguments("https://gitlab.com/group/sub/project.git", "group/sub/project")]
    [Arguments("ssh://git@gitlab.com:2222/group/project.git", "group/project")]
    [Arguments("https://github.com/VerifyTests/DiffEngine/", "VerifyTests/DiffEngine")]
    [Arguments("https://github.com", null)]
    [Arguments("", null)]
    [Arguments(null, null)]
    public async Task ReducesACloneUrlToItsPath(string? url, string? expected) =>
        await Assert.That(GitRemote.PathOf(url)).IsEqualTo(expected);

    [Test]
    public async Task ReadsTheOriginPastOtherRemotes()
    {
        using var directory = new TempDirectory();

        Config(
            directory,
            """
            [core]
            	repositoryformatversion = 0
            [remote "upstream"]
            	url = https://github.com/Someone/Fork.git
            [remote "origin"]
            	url = https://github.com/VerifyTests/DiffEngine.git
            	fetch = +refs/heads/*:refs/remotes/origin/*
            [branch "main"]
            	remote = origin
            """);

        await Assert.That(GitRemote.Of(directory)).IsEqualTo("VerifyTests/DiffEngine");
    }

    [Test]
    public async Task ACheckoutWithNoOriginHasNoRemote()
    {
        using var directory = new TempDirectory();
        Config(directory, "[core]\n\trepositoryformatversion = 0\n");

        await Assert.That(GitRemote.Of(directory)).IsNull();
    }

    [Test]
    public async Task ACheckoutWithNoConfigHasNoRemote()
    {
        using var directory = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(directory, ".git"));

        await Assert.That(GitRemote.Of(directory)).IsNull();
    }

    /// <summary>
    /// A worktree's .git is a file pointing at the main checkout, which is where its remotes are.
    /// </summary>
    [Test]
    public async Task FollowsTheGitdirOfAWorktree()
    {
        using var root = new TempDirectory();
        var main = Path.Combine(root, "main");
        Config(main, "[remote \"origin\"]\n\turl = git@github.com:VerifyTests/Verify.git\n");
        var worktree = Path.Combine(root, "feature");
        Directory.CreateDirectory(worktree);
        await File.WriteAllTextAsync(
            Path.Combine(worktree, ".git"),
            $"gitdir: {Path.Combine(main, ".git", "worktrees", "feature")}");

        await Assert.That(GitRemote.Of(worktree)).IsEqualTo("VerifyTests/Verify");
    }

    static void Config(string directory, string config)
    {
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        File.WriteAllText(Path.Combine(directory, ".git", "config"), config);
    }
}
