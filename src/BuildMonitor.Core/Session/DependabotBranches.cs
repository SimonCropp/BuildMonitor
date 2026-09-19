/// <summary>
/// What a row, a failure notification and the status verb call a Dependabot branch: a robot and the
/// package and version, so dependabot/nuget/src/Foo-1.0 reads 🤖 Foo-1.0. The prefix, the
/// ecosystem and the manifest's directory say nothing the package and version do not, and they
/// widened the column for every Dependabot row. The robot stands in for the prefix that went: without
/// it a row would claim a person pushed a branch called Foo-1.0.
/// <para>
/// Shortened only where the segment after the prefix is a known ecosystem, since that is what makes
/// the rest of the name Dependabot's own layout: a multi-ecosystem group puts its group name or target
/// branch there and a branch name template can put anything there, and a name cut down to its last
/// segment would lose the only part that identifies them.
/// </para>
/// </summary>
static class DependabotBranches
{
    const string prefix = "dependabot/";
    const string robot = "🤖";

    /// <summary>
    /// Both spellings, since GitHub's Dependabot writes the package manager (npm_and_yarn, go_modules)
    /// and the Azure DevOps extension writes the dependabot.yml ecosystem (npm, gomod). Matched ignoring
    /// case and reading a hyphen as an underscore, which covers dependabot.yml's github-actions and the
    /// branch-name-case and word-separator options.
    /// </summary>
    static readonly HashSet<string> ecosystems =
    [
        with(StringComparer.OrdinalIgnoreCase),
        "bazel",
        "bun",
        "bundler",
        "cargo",
        "composer",
        "conda",
        "deno",
        "devcontainers",
        "docker",
        "docker_compose",
        "dotnet_sdk",
        "elm",
        "gitsubmodule",
        "github_actions",
        "go_modules",
        "gomod",
        "gradle",
        "helm",
        "hex",
        "julia",
        "maven",
        "mix",
        "nix",
        "npm",
        "npm_and_yarn",
        "nuget",
        "opentofu",
        "pip",
        "pip_compile",
        "pipenv",
        "pnpm",
        "poetry",
        "pre_commit",
        "pub",
        "rust_toolchain",
        "sbt",
        "submodules",
        "swift",
        "terraform",
        "uv",
        "vcpkg",
        "yarn"
    ];

    public static string Short(string branch)
    {
        if (!Is(branch))
        {
            return branch;
        }

        return $"{robot} {branch[(branch.LastIndexOf('/') + 1)..]}";
    }

    /// <summary>
    /// Whether the name is one Dependabot laid out, which is the same question as whether anything
    /// may be cut off it: the robot stands for exactly the prefix that is dropped, so a branch that
    /// keeps its full name must not carry one either.
    /// </summary>
    static bool Is(string branch)
    {
        if (!branch.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var end = branch.IndexOf('/', prefix.Length);
        return end >= 0 &&
               ecosystems.Contains(branch[prefix.Length..end].Replace('-', '_'));
    }
}
