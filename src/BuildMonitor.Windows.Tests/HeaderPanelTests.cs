[TUnit.Core.Executors.STAThreadExecutor]
[NotInParallel(nameof(HeaderPanelTests))]
public class HeaderPanelTests
{
    [Test]
    public async Task TypingIsReportedAndApplyingIsNot()
    {
        using var panel = new HeaderPanel();
        panel.Apply("6 pipelines, 1 failing, 4 running", "");
        await Assert.That(panel.DrainSearch()).IsNull();

        var box = panel.Controls.OfType<TextBox>().Single();
        box.Text = "docs";
        await Assert.That(panel.DrainSearch()).IsEqualTo("docs");
        await Assert.That(panel.DrainSearch()).IsNull();

        // The session's text goes into a box without the focus and is not echoed back as an edit.
        panel.Apply("6 pipelines, 1 failing, 4 running", "main");
        await Assert.That(box.Text).IsEqualTo("main");
        await Assert.That(panel.DrainSearch()).IsNull();
    }

    [Test]
    public async Task ClearingEmptiesTheBoxOnlyWhenItHasText()
    {
        using var panel = new HeaderPanel();
        panel.Apply("6 pipelines, 1 failing, 4 running", "docs");
        await Assert.That(panel.ClearSearch()).IsTrue();
        await Assert.That(panel.DrainSearch()).IsEqualTo("");
        await Assert.That(panel.ClearSearch()).IsFalse();
    }
}
