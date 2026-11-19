/// <summary>
/// The AI apps a triage can open: the link each is handed, the prompts too long for one, which app
/// the setting picks, and how the options page shows and saves the choice.
/// </summary>
public class AssistantTests
{
    static readonly Assistant claude = Assistants.ById("claude")!;
    static readonly Assistant claudeCode = Assistants.ById("claude-code")!;
    static readonly Assistant cursor = Assistants.ById("cursor")!;

    // Escaped whole, so a line break, an & or a + in the prompt is not read as the end of a
    // parameter or a space, and a Windows path keeps its separators.
    [Test]
    public async Task TheClaudeAppTakesTheCheckoutAndThePrompt() =>
        await Assert.That(claude.Link("Fix it & go\n1+1", @"C:\Code\R&D")).IsEqualTo("claude://code/new?folder=C%3A%5CCode%5CR%26D&q=Fix%20it%20%26%20go%0A1%2B1");

    [Test]
    public async Task ClaudeCodeTakesTheCheckoutAndThePrompt() =>
        await Assert.That(claudeCode.Link("Fix it", "/code/Verify")).IsEqualTo("claude-cli://open?cwd=%2Fcode%2FVerify&q=Fix%20it");

    /// <summary>
    /// Cursor's link has nowhere to say a folder, so a triage without a checkout could still open
    /// it. The prompt names the checkout either way.
    /// </summary>
    [Test]
    public async Task CursorTakesThePromptAlone()
    {
        await Assert.That(cursor.Link("Fix it", "/code/Verify")).IsEqualTo("cursor://anysphere.cursor-deeplink/prompt?text=Fix%20it");
        await Assert.That(cursor.Link("Fix it", "")).IsEqualTo("cursor://anysphere.cursor-deeplink/prompt?text=Fix%20it");
    }

    /// <summary>
    /// A braced word in the prompt is escaped before it goes in, so it is not taken for the place
    /// the directory goes.
    /// </summary>
    [Test]
    public async Task APlaceholderInThePromptIsNotFilledIn() =>
        await Assert.That(claude.Link("{directory}", "/code")).IsEqualTo("claude://code/new?folder=%2Fcode&q=%7Bdirectory%7D");

    /// <summary>
    /// The Claude app keeps 14,336 characters of a prompt and drops the rest without saying so, and
    /// Claude Code keeps 5,000. Past either, the prompt is only copied rather than handed over short
    /// of its last steps.
    /// </summary>
    [Test]
    [Arguments("claude", 14_336)]
    [Arguments("claude-code", 5_000)]
    public async Task APromptPastWhatTheAppKeepsHasNoLink(string id, int longest)
    {
        var assistant = Assistants.ById(id)!;
        await Assert.That(assistant.Link(new('a', longest), "/code")).IsNotNull();
        await Assert.That(assistant.Link(new('a', longest + 1), "/code")).IsNull();
    }

    /// <summary>
    /// Cursor's limit is on the link as sent, so a prompt of spaces, each escaped to three
    /// characters, runs out at a third of the length a prompt of letters does.
    /// </summary>
    [Test]
    public async Task ALinkPastWhatCursorTakesIsNotMade()
    {
        await Assert.That(cursor.Link(new('a', 7_900), "")).IsNotNull();
        await Assert.That(cursor.Link(new(' ', 2_700), "")).IsNull();
    }

    /// <summary>
    /// An empty folder would open the session somewhere the prompt does not send it.
    /// </summary>
    [Test]
    public async Task AnAppThatOpensInTheCheckoutNeedsOne() =>
        await Assert.That(claude.Link("Fix it", "")).IsNull();

    [Test]
    public async Task TheDefaultTakesTheFirstFound()
    {
        await Assert.That(Assistants.Resolve("", [claudeCode, cursor])).IsEqualTo(claudeCode);
        await Assert.That(Assistants.Resolve("", [])).IsNull();
    }

    [Test]
    public async Task TheClipboardOpensNothing() =>
        await Assert.That(Assistants.Resolve(Assistants.Clipboard, [claude])).IsNull();

    /// <summary>
    /// An app chosen and since removed leaves the prompt on the clipboard rather than opening
    /// another app nobody chose.
    /// </summary>
    [Test]
    public async Task AChosenAppIsOnlyOpenedWhileItIsFound()
    {
        await Assert.That(Assistants.Resolve("cursor", [claude, cursor])).IsEqualTo(cursor);
        await Assert.That(Assistants.Resolve("cursor", [claude])).IsNull();
    }

    /// <summary>
    /// A later version may know an app this one does not. Its id is taken as the default rather
    /// than turning the feature off.
    /// </summary>
    [Test]
    public async Task AnUnknownIdIsTheDefault()
    {
        await Assert.That(Assistants.Resolve("some-later-app", [cursor])).IsEqualTo(cursor);
        await Assert.That(Assistants.Label("some-later-app")).IsEqualTo(Assistants.FirstFoundLabel);
    }

    [Test]
    public async Task FoundInTheirOwnOrder() =>
        await Assert.That(Assistants.Find(_ => _ is "cursor" or "claude")).IsEquivalentTo([claude, cursor]);

    [Test]
    [Arguments("", "First AI app found")]
    [Arguments("clipboard", "Clipboard only")]
    [Arguments("claude", "Claude")]
    [Arguments("claude-code", "Claude Code in a terminal")]
    [Arguments("cursor", "Cursor")]
    public async Task EachSettingHasALabelThatSavesBackToIt(string setting, string label)
    {
        await Assert.That(Assistants.Label(setting)).IsEqualTo(label);
        await Assert.That(Assistants.Setting(label)).IsEqualTo(setting);
    }

    [Test]
    public async Task TheOptionsListWhatIsFound() =>
        await Assert.That(Assistants.Labels([claude], Assistants.FirstFoundLabel)).IsEquivalentTo(["First AI app found", "Clipboard only", "Claude"]);

    /// <summary>
    /// The choice saved stays listed once its app has gone, so the drop down shows the setting as
    /// it is rather than a value it has no choice for.
    /// </summary>
    [Test]
    public async Task AChosenAppNoLongerFoundStaysListed() =>
        await Assert.That(Assistants.Labels([claude], "Cursor")).IsEquivalentTo(["First AI app found", "Clipboard only", "Claude", "Cursor"]);

    [Test]
    public async Task TheOptionsShowAndSaveTheChoice()
    {
        var state = Fixtures.WithBuilds();
        state = state with
        {
            Settings = state.Settings with
            {
                TriageAssistant = "cursor"
            },
            InstalledAssistants = [claude]
        };
        state = MonitorSession.OpenOptions(state);
        var field = ScreenBuilder.Build(state, Fixtures.Now).Form!.Fields.Single(_ => _.Id == FormFields.TriageAssistant);
        await Assert.That(field.Value).IsEqualTo("Cursor");
        await Assert.That(field.Options!).IsEquivalentTo(["First AI app found", "Clipboard only", "Claude", "Cursor"]);

        state = MonitorSession.FieldChanged(state, FormFields.TriageAssistant, "Clipboard only");
        await Assert.That(OptionsDraft.TryBuild(Fixtures.OptionsForm(state), state.Settings, out var saved, out _)).IsTrue();
        await Assert.That(saved!.TriageAssistant).IsEqualTo(Assistants.Clipboard);
    }

    /// <summary>
    /// Looked for again on the way in, so an app installed since the tray started is offered.
    /// </summary>
    [Test]
    public async Task OpeningTheOptionsLooksForApps()
    {
        var actions = new RecordingActions();
        InputApplier.Apply(Fixtures.WithBuilds(), new(Key: CommandKind.OpenOptions), actions.Actions, new FakeWindow());
        await Assert.That(actions.Calls).Contains("FindAssistants");
    }

    [Test]
    public async Task FindingTheSameAppsKeepsTheState()
    {
        var state = MonitorSession.FoundAssistants(Fixtures.WithBuilds(), [claude]);
        await Assert.That(state.InstalledAssistants).IsEquivalentTo([claude]);
        await Assert.That(MonitorSession.FoundAssistants(state, [claude])).IsSameReferenceAs(state);
    }
}
