public class CommandLineTests
{
    [Test]
    [Arguments("")]
    [Arguments("show")]
    [Arguments("--help")]
    [Arguments("status")]
    [Arguments("mcp")]
    [Arguments("refresh gh")]
    [Arguments("--version")]
    [Arguments("dance")]
    public async Task Parses(string line)
    {
        var args = line.Length == 0 ? [] : line.Split(' ');
        await Verify(CommandLine.Parse(args)).UseTextForParameters(line.Length == 0 ? "none" : line.Replace(' ', '_').Replace("--", ""));
    }
}
