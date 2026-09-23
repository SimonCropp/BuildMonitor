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
        // Whichever box the page puts first, rather than a field id that has to be kept in step
        // with the order of the options page.
        var first = screen.Form!.Fields.First(_ => _.Kind is FieldKind.Text or FieldKind.Password or FieldKind.Number);
        text.Text = "45";
        var changes = panel.DrainChanges()!;
        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Id).IsEqualTo(first.Id);
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
        var screen = ScreenBuilder.Build(Fixtures.Filters(), Fixtures.Now);
        panel.Apply(screen.Form!);
        var add = Descendants(panel).OfType<FormsButton>().First(_ => _.Text == "Add filter");
        add.PerformClick();
        await Assert.That(panel.DrainClickedField()).IsEqualTo(FormFields.AddFilter);
        await Assert.That(panel.DrainClickedField()).IsNull();
    }

    /// <summary>
    /// The row itself, not a cross beside it: a connection row opens its editor, and the head drew
    /// the only click target as a remove button.
    /// </summary>
    [Test]
    public async Task ConnectionRowsReportTheirField()
    {
        using var panel = new FormPanel();
        var screen = ScreenBuilder.Build(Fixtures.Connections(), Fixtures.Now);
        panel.Apply(screen.Form!);
        var rows = Descendants(panel).OfType<EditRowLink>().ToList();
        await Assert.That(rows.Select(_ => _.Text)).IsEquivalentTo(["GitHub: GitHub Actions, ok", "Jenkins: Jenkins, ok", "Octopus: Octopus Deploy, ok"]);

        rows[1].PerformClick();
        await Assert.That(panel.DrainClickedField()).IsEqualTo(FormFields.Connection(Fixtures.Jenkins.Id));
    }

    /// <summary>
    /// Every line of the update page, though the lines about the MCP servers share an id. Keyed by
    /// id, all but the last of them were left empty, and each empty one still held its row.
    /// </summary>
    [Test]
    public async Task LinesSharingAnIdEachShowTheirOwn()
    {
        using var panel = new FormPanel();
        var form = ScreenBuilder.Build(Fixtures.Update(), Fixtures.Now).Form!;
        panel.Apply(form);
        // Past the version, the one field drawn with its label in front of its value.
        var shown = Descendants(panel).OfType<Label>().Select(_ => _.Text).Skip(1);
        var lines = form.Fields.Skip(1).Select(_ => _.Value);
        await Assert.That(shown).IsEquivalentTo(lines, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
