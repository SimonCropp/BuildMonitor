/// <summary>
/// An AI app that takes a prompt through a link it registers with the desktop, so a triage can
/// open it with the prompt already typed rather than leave it on the clipboard to paste.
/// <para>
/// Found by its link's scheme rather than by where it is installed: the link is what is about to
/// be opened, and an app whose scheme nothing answers opens nothing however it was installed. The
/// Claude desktop app is a packaged app on Windows, with no executable on any path to look for.
/// </para>
/// </summary>
/// <param name="Id">What settings.json keeps for it, so a rename of <paramref name="Name"/> does not
/// lose the choice.</param>
/// <param name="Name">What the options and the status line call it.</param>
/// <param name="Scheme">The scheme of its link, which is how it is found.</param>
/// <param name="Template">The link, with <c>{prompt}</c> and <c>{directory}</c> where the escaped
/// prompt and the checkout it is about go.</param>
/// <param name="MaxPrompt">The longest prompt it keeps whole, counted as decoded text. The Claude app
/// cuts a longer one short without a word, which loses the steps at the end.</param>
/// <param name="MaxLink">The longest link it takes, counted escaped.</param>
record Assistant(string Id, string Name, string Scheme, string Template, int MaxPrompt, int MaxLink)
{
    /// <summary>
    /// The link for <paramref name="prompt"/>, or null where it would not arrive whole. Null rather
    /// than cut short: the steps come last, and a prompt missing the one that says to stop and ask
    /// reads as complete, so an assistant carries on into changes nobody asked for.
    /// </summary>
    public string? Link(string prompt, string directory)
    {
        if (prompt.Length > MaxPrompt)
        {
            return null;
        }

        // A link with an empty folder opens a session somewhere else, which the prompt then sends
        // looking for a checkout it names but is not in.
        if (directory.Length == 0 &&
            Template.Contains("{directory}"))
        {
            return null;
        }

        // Both escaped before they go in, which turns a brace into %7B, so neither can carry a
        // placeholder for the other's Replace to find.
        var link = Template
            .Replace("{directory}", Uri.EscapeDataString(directory))
            .Replace("{prompt}", Uri.EscapeDataString(prompt));
        if (link.Length > MaxLink)
        {
            return null;
        }

        return link;
    }
}
