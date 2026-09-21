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

        var box = Box(panel);
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

    /// <summary>
    /// The cross shows only while there is something to clear, and clicking it reports the empty
    /// box as the edit it is, the way typing the text away would.
    /// </summary>
    [Test]
    public async Task TheCrossShowsWithTextAndClearsIt()
    {
        using var panel = new HeaderPanel();
        var cross = Cross(panel);
        panel.Apply("6 pipelines, 1 failing, 4 running", "");
        await Assert.That(cross.Visible).IsFalse();

        panel.Apply("6 pipelines, 1 failing, 4 running", "docs");
        await Assert.That(cross.Visible).IsTrue();
        panel.DrainSearch();

        cross.PerformClick();
        await Assert.That(Box(panel).Text).IsEqualTo("");
        await Assert.That(panel.DrainSearch()).IsEqualTo("");
        await Assert.That(cross.Visible).IsFalse();
    }

    static TextBox Box(HeaderPanel panel) =>
        Descendants(panel).OfType<TextBox>().Single();

    static FormsButton Cross(HeaderPanel panel) =>
        Descendants(panel).OfType<FormsButton>().Single(_ => _.Text == "×");

    static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }
}
