/// <summary>
/// The robot that stands in for a bot wherever a row names one: the Dependabot prefix a branch
/// drops, and the login of the app that pushed a build. One mark and one rule for what counts as a
/// bot, so a row cannot mark the branch and call the author who pushed it something else.
/// </summary>
static class Bots
{
    public const string Mark = "\U0001F916";

    const string suffix = "[bot]";

    /// <summary>
    /// The app's name without the [bot] GitHub adds to every app login, or null for a person. The
    /// suffix is the only thing in an author that says it is not one, so a bot a service reports
    /// under a bare name is treated as a person; guessing from the name would eventually call
    /// someone a robot.
    /// <para>
    /// Read from the first word, since a provider may follow the login with an address:
    /// "github-actions[bot] &lt;bot@users.noreply.github.com&gt;".
    /// </para>
    /// </summary>
    public static string? NameOf(string author)
    {
        var space = author.IndexOf(' ');
        var first = space < 0 ? author : author[..space];
        if (first.EndsWith(suffix, StringComparison.Ordinal))
        {
            return first[..^suffix.Length];
        }

        return null;
    }
}
