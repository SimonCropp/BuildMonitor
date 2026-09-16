/// <summary>
/// What a log is cut down to before it is answered with: the end of every section, and a count
/// of what came before it.
/// </summary>
public class LogTailTests
{
    [Test]
    public async Task ALogInsideTheSizeIsUnchanged()
    {
        var log = "==> build <==\nerror CS1002: ; expected";
        await Assert.That(LogTail.Take(log, 200)).IsEqualTo(log);
    }

    [Test]
    public async Task ALogWithNoSectionsIsTailedWhole()
    {
        var log = "one\ntwo\nthree";
        await Assert.That(LogTail.Take(log, 2)).IsEqualTo("... 1 earlier line dropped\ntwo\nthree");
    }

    [Test]
    public async Task EverySectionKeepsItsOwnEnd()
    {
        var log = """
                  ==> build (windows-latest) <==
                  restoring
                  building
                  error CS1002: ; expected
                  the build failed

                  ==> docs <==
                  the job has exceeded the maximum execution time
                  """;
        await Verify(LogTail.Take(log, 2))
            .Snapshot(
                """
                ==> build (windows-latest) <==
                ... 2 earlier lines dropped
                error CS1002: ; expected
                the build failed

                ==> docs <==
                the job has exceeded the maximum execution time
                """);
    }

    [Test]
    public async Task ASizeBelowOneStillLeavesALine()
    {
        var log = "==> build <==\none\ntwo";
        await Assert.That(LogTail.Take(log, 0)).IsEqualTo("==> build <==\n... 1 earlier line dropped\ntwo");
    }

    [Test]
    public async Task WindowsLineEndingsAreSectionedTheSameWay()
    {
        var log = "==> build <==\r\none\r\ntwo\r\n\r\n==> docs <==\r\nthree";
        await Assert.That(LogTail.Take(log, 1)).IsEqualTo("==> build <==\r\n... 1 earlier line dropped\ntwo\r\n\n==> docs <==\r\nthree");
    }
}
