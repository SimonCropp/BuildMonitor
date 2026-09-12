[TUnit.Core.Executors.STAThreadExecutor]
[NotInParallel(nameof(FormPanelTests))]
public class FormPanelTests
{
    [Test]
    public async Task TypingRaisesAChangeAndApplyDoesNotFightIt()
    {
        using var panel = new FormPanel();
        var screen = ScreenBuilder.Build(Fixtures.Options(), Fixtures.Now);
        panel.Apply(screen.Form!);
        var text = Descendants(panel).OfType<TextBox>().First();
        text.Text = "45";
        var changes = panel.DrainChanges()!;
        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Id).IsEqualTo(FormFields.PollInterval);
        await Assert.That(changes[0].Value).IsEqualTo("45");

        // A re-apply of the same values does not report a change.
        panel.Apply(screen.Form!);
        await Assert.That(panel.DrainChanges()).IsNull();
    }

    [Test]
    public async Task SelectsShowTheirValue()
    {
        using var panel = new FormPanel();
        var screen = ScreenBuilder.Build(Fixtures.ConnectionNew(), Fixtures.Now);
        panel.Apply(screen.Form!);
        var select = Descendants(panel).OfType<SelectControl>().First();
        await Assert.That(select.Value).IsEqualTo("AppVeyor");
        await Assert.That(select.Options.Count).IsEqualTo(ProviderDescriptors.All.Count);
    }

    [Test]
    public async Task ButtonsReportTheirField()
    {
        using var panel = new FormPanel();
        var screen = ScreenBuilder.Build(Fixtures.Options(), Fixtures.Now);
        panel.Apply(screen.Form!);
        var add = Descendants(panel).OfType<FormsButton>().First(_ => _.Text == "Add connection");
        add.PerformClick();
        await Assert.That(panel.DrainClickedField()).IsEqualTo(FormFields.AddConnection);
        await Assert.That(panel.DrainClickedField()).IsNull();
    }

    [Test]
    public async Task ControlsAreRebuiltOnlyWhenFieldsChange()
    {
        using var panel = new FormPanel();
        var options = ScreenBuilder.Build(Fixtures.Options(), Fixtures.Now).Form!;
        panel.Apply(options);
        var before = Descendants(panel).OfType<TextBox>().First();
        panel.Apply(options);
        var same = Descendants(panel).OfType<TextBox>().First();
        await Assert.That(same).IsSameReferenceAs(before);

        panel.Apply(ScreenBuilder.Build(Fixtures.Filters(), Fixtures.Now).Form!);
        var rebuilt = Descendants(panel).OfType<TextBox>().First();
        await Assert.That(rebuilt).IsNotSameReferenceAs(before);
    }

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
