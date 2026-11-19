/// <summary>
/// Hands a triage prompt to the app the options pick, where it fits that app's link, and says what
/// became of it. The prompt is copied whichever way this goes, so the status line only has to say
/// whether an app has it too, and if not, why: someone who expected the app to come forward and
/// finds nothing happened needs to hear the prompt is on the clipboard instead.
/// </summary>
static class TriageHandoff
{
    /// <param name="name">The build, as the status line names it.</param>
    /// <param name="open">Opens a link, false where the desktop would not.</param>
    /// <returns>The app now holding the prompt, or null, and how the status line starts.</returns>
    public static (Assistant? Opened, string Lead) Hand(string name, string prompt, string directory, Assistant? assistant, Func<string, bool> open)
    {
        var copied = $"Copied a triage prompt for {name}";
        if (assistant is null)
        {
            return (null, copied);
        }

        if (assistant.Link(prompt, directory) is not { } link)
        {
            return (null, $"{copied}, too long to open in {assistant.Name}");
        }

        if (!open(link))
        {
            return (null, $"{copied}, since {assistant.Name} would not open");
        }

        return (assistant, $"Opened a triage prompt for {name} in {assistant.Name}, and copied it");
    }
}
