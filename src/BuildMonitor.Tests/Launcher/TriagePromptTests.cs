/// <summary>
/// The text the triage command hands an assistant, verified as text rather than through an MCP
/// client, which is what the projection being pure buys.
/// </summary>
public class TriagePromptTests
{
    [Test]
    public Task RepositoriesSharingAPipelineAreGatheredUnderIt() =>
        Verify(TriagePrompt.Build([..Dependabot(), Solo()], fix: false, null));

    [Test]
    public Task FailuresWithNoCheckoutAreNamedRatherThanDropped() =>
        Verify(TriagePrompt.Build([Solo(), ..Deployments()], fix: false, null))
            .Snapshot(
                """
                One failing build has its code checked out locally. Work through it.

                ## The failures

                ### test.yml

                - VerifyTests/Verify on main, run 412, failed 2h ago
                  code: /code/Verify
                  key: gh/Verify/test.yml/main
                  commit 9f1c2b7 by Simon Cropp: Tighten the converter lookup

                ## How to work through them

                1. Read each build's log with `get_build_log`, taking the key from its entry above.
                2. Group the failures by what the logs actually say before investigating any of them. Repositories failing on one shared workflow, action, dependency or template are one fix, not several, and the pipeline groupings above are only a guess at that.
                3. Work each group from the checkout named under `code:`. That checkout is the user's own and may be on another branch or hold uncommitted work, so never switch its branch, stash or discard anything in it. Where the build ran on a branch other than the one checked out, fetch it and add a `git worktree` for it instead. Reproduce the failure before deciding what it is.
                4. Report what you found per group, with the fix you would make. Do not change any source files.

                Where a group turns out not to be code at all, such as an expired credential, a runner or agent problem, or a service outage, report it as exactly that rather than looking for a change to make.

                2 other failing builds have no checkout under the code directory and are left out above:

                - Invitations e2e on Pmc Dev Octopus, failed 1h ago
                - LBS Client on Pmc Dev Octopus, failed 7d ago
                """);

    /// <summary>
    /// The state every user is in until they set a code directory, so it has to read as an answer
    /// rather than as a clean build list.
    /// </summary>
    [Test]
    public Task NoCheckoutAtAllPointsAtTheCodeDirectoryOption() =>
        Verify(TriagePrompt.Build(Deployments(), fix: false, null))
            .Snapshot(
                """
                2 failing builds, none with a repository checked out locally, so there is no code here to work through.

                - Invitations e2e on Pmc Dev Octopus, failed 1h ago
                - LBS Client on Pmc Dev Octopus, failed 7d ago

                Either the code directory option is not set, or no checkout under it matched these repositories. Tell the user to set it from the tray's options, then run this again.
                """);

    /// <summary>
    /// Compared line by line against the text without it, since a snapshot of the fixing text alone
    /// would pass just as well if the flag had also loosened the rule about the user's checkout.
    /// </summary>
    [Test]
    public async Task AskingForAFixChangesTheLastStepAndNothingElse()
    {
        var reporting = TriagePrompt.Build([Solo()], fix: false, null).Split('\n');
        var fixing = TriagePrompt.Build([Solo()], fix: true, null).Split('\n');

        var changed = reporting
            .Zip(fixing)
            .Where(_ => _.First != _.Second)
            .Select(_ => _.Second)
            .ToList();
        await Assert.That(fixing.Length).IsEqualTo(reporting.Length);
        await Assert.That(changed).IsEquivalentTo(["4. Fix each group where you reproduced it, and run that project's tests. Leave the changes uncommitted, say where they are so the user can review them, and do not commit, push or open a pull request."]);
    }

    [Test]
    public async Task NothingFailingNeedsNoProcedure()
    {
        await Assert.That(TriagePrompt.Build([], fix: false, null))
            .IsEqualTo("Nothing is failing. There is no triage to do.");
        await Assert.That(TriagePrompt.Build([], fix: false, "octopus"))
            .IsEqualTo("Nothing is failing matching \"octopus\". There is no triage to do.");
    }

    [Test]
    public async Task TheFilterIsNamedSoTheScopeOfTheAnswerIsNotGuessedAt()
    {
        var text = TriagePrompt.Build([Solo()], fix: false, "verify");

        await Assert.That(text).StartsWith("One failing build matching \"verify\" has its code checked out locally. Work through it.");
    }

    /// <summary>
    /// A prompt's arguments are strings on the wire, so this is what a client actually sends for a
    /// flag. Only a plain yes turns it on: Claude Code binds arguments by position, so a repository
    /// name typed one slot too far arrives here, and it must not start anything editing.
    /// </summary>
    [Test]
    [Arguments("true", true)]
    [Arguments("True", true)]
    [Arguments(" yes ", true)]
    [Arguments("on", true)]
    [Arguments("1", true)]
    [Arguments(null, false)]
    [Arguments("", false)]
    [Arguments("false", false)]
    [Arguments("no", false)]
    [Arguments("0", false)]
    [Arguments("SdkCheck", false)]
    [Arguments("please", false)]
    public async Task FixIsOnOnlyForAPlainYes(string? value, bool expected) =>
        await Assert.That(TriagePrompt.Fixing(value)).IsEqualTo(expected);

    /// <summary>
    /// A star in the filter's slot is how every build is asked for when the fix argument after it
    /// has to be given too.
    /// </summary>
    [Test]
    [Arguments(null, null)]
    [Arguments("", null)]
    [Arguments("  ", null)]
    [Arguments("*", null)]
    [Arguments(" * ", null)]
    [Arguments("SdkCheck", "SdkCheck")]
    [Arguments(" octopus ", "octopus")]
    public async Task AStarOrNothingFiltersNothing(string? value, string? expected) =>
        await Assert.That(TriagePrompt.Filter(value)).IsEqualTo(expected);

    /// <summary>
    /// The case the command exists for: one broken workflow, three repositories, and a commit body
    /// long enough to bury the row it sits on.
    /// </summary>
    static List<BuildDto> Dependabot() =>
    [
        Failing(
            "gh/EfQueryComplexity/merge-dependabot/tunit",
            "SimonCropp/EfQueryComplexity",
            "merge-dependabot",
            "/code/EfQueryComplexity",
            branch: "dependabot/nuget/src/TUnit-1.68.0",
            commit: "1b048372d87a90cbcd732bbaf1ad5f675b668051",
            commitMessage: "Bump TUnit from 1.67.0 to 1.68.0\n\n---\nupdated-dependencies:\n- dependency-name: TUnit\n\nSigned-off-by: dependabot[bot]",
            author: "dependabot[bot]"),
        Failing(
            "gh/SdkCheck/merge-dependabot/testsdk",
            "SimonCropp/SdkCheck",
            "merge-dependabot",
            "/code/SdkCheck",
            branch: "dependabot/nuget/src/Microsoft.NET.Test.Sdk-18.10.1",
            run: "17",
            timing: "14h ago",
            commit: "e776839ea1b313ffac2ebfee9dc57fab10b43c7e",
            commitMessage: "Bump Microsoft.NET.Test.Sdk from 18.9.0 to 18.10.1",
            author: "dependabot[bot]"),
        Failing(
            "gh/EntityFramework.OrderBy/merge-dependabot/verify",
            "SimonCropp/EntityFramework.OrderBy",
            "merge-dependabot",
            "/code/EntityFramework.OrderBy",
            branch: "dependabot/nuget/src/multi-a9bd3edf79",
            run: "156",
            timing: "20h ago",
            commit: "ad2a133c23dcd6833646c1da7b150ebb7ccd865e",
            commitMessage: "Bump Verify and Verify.NUnit",
            author: "dependabot[bot]")
    ];

    static BuildDto Solo() =>
        Failing(
            "gh/Verify/test.yml/main",
            "VerifyTests/Verify",
            "test.yml",
            "/code/Verify",
            branch: "main",
            run: "412",
            timing: "2h ago",
            commit: "9f1c2b7a",
            commitMessage: "Tighten the converter lookup",
            author: "Simon Cropp");

    /// <summary>
    /// A deployment of an already built release: no commit, no repository, and so never a checkout.
    /// </summary>
    static List<BuildDto> Deployments() =>
    [
        Failing("octopus/Projects-281/e2e", "Invitations e2e", "Invitations e2e", null, branch: "e2e Testing", run: "0.0.1660", timing: "1h ago", connection: "Pmc Dev Octopus"),
        Failing("octopus/Projects-26/Dev", "LBS Client", "LBS Client", null, branch: "Dev", run: "1.0.64445", timing: "7d ago", connection: "Pmc Dev Octopus")
    ];

    static BuildDto Failing(
        string key,
        string repo,
        string pipeline,
        string? directory,
        string? branch = null,
        string run = "1",
        string timing = "6h ago",
        string? commit = null,
        string? commitMessage = null,
        string? author = null,
        string connection = "GitHub SimonCropp") =>
        new(
            key,
            connection,
            pipeline,
            repo,
            branch,
            run,
            nameof(BuildStatus.Failed),
            "failure",
            null,
            null,
            -1,
            timing,
            $"https://ci.example/{key}",
            null,
            null,
            null,
            commit,
            commitMessage,
            author,
            true,
            false,
            directory);
}
