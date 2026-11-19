/// <summary>
/// The AI apps a triage can open, which of them this machine has, and which one a triage opens.
/// <para>
/// Only apps whose link both takes a prompt and leaves it for the user to send. Every one here
/// fills its prompt box and waits: a link that ran its prompt on arrival would have a triage
/// start work in a checkout before anyone had read what it was told.
/// </para>
/// </summary>
static class Assistants
{
    /// <summary>
    /// The setting that keeps a triage on the clipboard even where an app is found.
    /// </summary>
    public const string Clipboard = "clipboard";

    /// <summary>
    /// The setting's default, and its label in the options: the first of <see cref="Known"/> that
    /// is found, so an app installed later is picked up without a visit to the options.
    /// </summary>
    public const string FirstFoundLabel = "First AI app found";

    public const string ClipboardLabel = "Clipboard only";

    /// <summary>
    /// In the order the first found is taken from. The Claude app and Claude Code take the checkout
    /// with the prompt and start there, which is where the prompt sends the assistant anyway.
    /// Cursor's link has nowhere to say a folder, so it opens in whatever it had open last.
    /// <para>
    /// The Claude app keeps 14,336 characters of a prompt and drops the folder from a link that
    /// takes its command line past 26,624 bytes, which the link's limit leaves room under for the
    /// app's own path. Claude Code keeps 5,000. Cursor takes links up to 10,000 characters, and
    /// 8,000 before that.
    /// </para>
    /// </summary>
    public static readonly ImmutableArray<Assistant> Known =
    [
        new("claude", "Claude", "claude", "claude://code/new?folder={directory}&q={prompt}", MaxPrompt: 14_336, MaxLink: 24_000),
        new("claude-code", "Claude Code in a terminal", "claude-cli", "claude-cli://open?cwd={directory}&q={prompt}", MaxPrompt: 5_000, MaxLink: 24_000),
        new("cursor", "Cursor", "cursor", "cursor://anysphere.cursor-deeplink/prompt?text={prompt}", MaxPrompt: int.MaxValue, MaxLink: 8_000)
    ];

    /// <summary>
    /// The apps this machine has a handler for, in <see cref="Known"/> order. Replaceable so a test
    /// never asks the desktop.
    /// </summary>
    public static Func<ImmutableArray<Assistant>> Installed { get; set; } = () => Find(LinkHandlers.Exists);

    public static ImmutableArray<Assistant> Find(Func<string, bool> hasHandler) =>
        [..Known.Where(_ => hasHandler(_.Scheme))];

    /// <summary>
    /// The app a triage opens, or null to leave it on the clipboard alone. One named in the setting
    /// that is no longer found falls back to the clipboard rather than to whatever else is found:
    /// a prompt that opens in an app nobody chose reads as something having gone wrong.
    /// </summary>
    public static Assistant? Resolve(string setting, ImmutableArray<Assistant> installed)
    {
        if (setting == Clipboard)
        {
            return null;
        }

        if (ById(setting) is { } chosen)
        {
            return installed.FirstOrDefault(_ => _.Id == chosen.Id);
        }

        // Empty, or an id this version does not know, as one written by a later version would be.
        return installed.FirstOrDefault();
    }

    public static Assistant? ById(string id) =>
        Known.FirstOrDefault(_ => _.Id == id);

    /// <summary>
    /// What the options' drop down shows for a setting. An id this version does not know shows as
    /// the default, which is also what <see cref="Resolve"/> takes it as.
    /// </summary>
    public static string Label(string setting)
    {
        if (setting == Clipboard)
        {
            return ClipboardLabel;
        }

        if (ById(setting) is { } assistant)
        {
            return assistant.Name;
        }

        return FirstFoundLabel;
    }

    /// <summary>
    /// The setting a label in the drop down stands for: the reverse of <see cref="Label"/>.
    /// </summary>
    public static string Setting(string label)
    {
        if (label == ClipboardLabel)
        {
            return Clipboard;
        }

        if (Known.FirstOrDefault(_ => _.Name == label) is { } assistant)
        {
            return assistant.Id;
        }

        return "";
    }

    /// <summary>
    /// The drop down's choices: the default, the clipboard, and each app that is found. An app the
    /// setting names that is no longer found stays listed, as <paramref name="selected"/>, so the
    /// drop down shows the setting as it is rather than a choice it does not hold.
    /// <para>
    /// No note beside it saying which app the default is now: no head draws a select's note, and
    /// the Windows one fixes a select's choices when it makes it. The triage chip's hover says it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Labels(ImmutableArray<Assistant> installed, string selected) =>
    [
        FirstFoundLabel,
        ClipboardLabel,
        ..Known
            .Where(_ => installed.Contains(_) || _.Name == selected)
            .Select(_ => _.Name)
    ];
}
