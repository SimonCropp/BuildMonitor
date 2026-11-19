/// <summary>
/// What the status line says became of a triage prompt. It is copied whichever way this goes, so
/// each line says the copy happened, and where no app has it, why not.
/// </summary>
public class TriageHandoffTests
{
    static readonly Assistant claude = Assistants.ById("claude")!;

    [Test]
    public async Task OpenedInTheApp()
    {
        List<string> opened = [];
        var (assistant, lead) = TriageHandoff.Hand("test.yml #77", "Fix it", "/code/Verify", claude, _ =>
        {
            opened.Add(_);
            return true;
        });
        await Assert.That(assistant).IsEqualTo(claude);
        await Assert.That(lead).IsEqualTo("Opened a triage prompt for test.yml #77 in Claude, and copied it");
        await Assert.That(opened).IsEquivalentTo(["claude://code/new?folder=%2Fcode%2FVerify&q=Fix%20it"]);
    }

    [Test]
    public async Task NoAppOnlyCopies()
    {
        var (assistant, lead) = TriageHandoff.Hand("test.yml #77", "Fix it", "/code/Verify", null, _ => throw new("Nothing to open"));
        await Assert.That(assistant).IsNull();
        await Assert.That(lead).IsEqualTo("Copied a triage prompt for test.yml #77");
    }

    [Test]
    public async Task TooLongForTheAppIsOnlyCopied()
    {
        var (assistant, lead) = TriageHandoff.Hand("test.yml #77", new('a', 20_000), "/code/Verify", claude, _ => throw new("Nothing to open"));
        await Assert.That(assistant).IsNull();
        await Assert.That(lead).IsEqualTo("Copied a triage prompt for test.yml #77, too long to open in Claude");
    }

    [Test]
    public async Task AnAppThatWouldNotOpenLeavesTheCopy()
    {
        var (assistant, lead) = TriageHandoff.Hand("test.yml #77", "Fix it", "/code/Verify", claude, _ => false);
        await Assert.That(assistant).IsNull();
        await Assert.That(lead).IsEqualTo("Copied a triage prompt for test.yml #77, since Claude would not open");
    }
}
