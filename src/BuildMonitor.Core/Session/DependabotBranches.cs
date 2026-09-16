/// <summary>
/// What a row, a failure notification and the status verb call a Dependabot branch: the name without
/// the ecosystem Dependabot puts second, so dependabot/nuget/src/Foo-1.0 reads dependabot/src/Foo-1.0.
/// The package already says which ecosystem it is, and the segment widened the column for every
/// Dependabot row. Only a known ecosystem is dropped, not whatever comes second: a multi-ecosystem
/// group puts its group name or target branch there, a branch name template can put anything there,
/// and the name would lose it.
/// </summary>
static class DependabotBranches
{
    const string prefix = "dependabot/";

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
        if (!branch.StartsWith(prefix, StringComparison.Ordinal))
        {
            return branch;
        }

        var end = branch.IndexOf('/', prefix.Length);
        if (end < 0 ||
            !ecosystems.Contains(branch[prefix.Length..end].Replace('-', '_')))
        {
            return branch;
        }

        return prefix + branch[(end + 1)..];
    }
}
