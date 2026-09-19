public class DependabotBranchesTests
{
    [Test]
    [Arguments("dependabot/nuget/src/Syncfusion.XlsIO.Net.Core-31.1.17", "🤖 Syncfusion.XlsIO.Net.Core-31.1.17")]
    [Arguments("dependabot/github_actions/actions/checkout-5", "🤖 checkout-5")]
    [Arguments("dependabot/npm_and_yarn/next/eslint-bb065a57ed", "🤖 eslint-bb065a57ed")]
    // The Azure DevOps extension's dependabot.yml spellings
    [Arguments("dependabot/github-actions/main/actions/checkout-5", "🤖 checkout-5")]
    [Arguments("dependabot/gomod/golang.org/x/net-0.30.0", "🤖 net-0.30.0")]
    // The word-separator and branch-name-case options
    [Arguments("dependabot/npm-and-yarn/lodash-4.17.21", "🤖 lodash-4.17.21")]
    [Arguments("dependabot/NUGET/SRC/SYNCFUSION.XLSIO-31.1.17", "🤖 SYNCFUSION.XLSIO-31.1.17")]
    public async Task KeepsThePackageAndVersionAlone(string branch, string expected) =>
        await Assert.That(DependabotBranches.Short(branch)).IsEqualTo(expected);

    [Test]
    // Multi-ecosystem groups, alone and with a target branch
    [Arguments("dependabot/infrastructure-3fa9c1e2b4")]
    [Arguments("dependabot/develop/infrastructure-3fa9c1e2b4")]
    // The template "{prefix}/infra/{name}"
    [Arguments("dependabot/infra/infrastructure-3fa9c1e2b4")]
    // A separator or prefix other than the default
    [Arguments("dependabot-nuget-src-Syncfusion.XlsIO.Net.Core-31.1.17")]
    [Arguments("dependabot_cargo_crc32fast-1.5.2")]
    [Arguments("deps/nuget/src/Syncfusion.XlsIO.Net.Core-31.1.17")]
    [Arguments("dependabot/nuget")]
    [Arguments("renovate/nuget-packages")]
    [Arguments("feature/nuget")]
    [Arguments("main")]
    [Arguments("")]
    public async Task KeepsOtherBranches(string branch) =>
        await Assert.That(DependabotBranches.Short(branch)).IsEqualTo(branch);
}
