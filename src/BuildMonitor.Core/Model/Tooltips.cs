/// <summary>
/// Line breaks for the text a hover shows. A tooltip is read at a glance, and the longest of them
/// ran to ninety characters on one line, which is a line the eye has to track back along; the
/// triage chip's carries a checkout path that can be as long again on its own.
/// <para>
/// Wrapped here rather than by each head, for the reason every other string on a screen is
/// composed here: three heads draw in three fonts and none of them wraps a tooltip by itself, so
/// each would have had to work out its own breaks and they would not agree. The measure is in
/// characters for the same reason. It is a reading width rather than a fitting one, so a word
/// longer than it, which a path usually is, is left whole on a line of its own rather than broken.
/// </para>
/// </summary>
static class Tooltips
{
    const int width = 48;

    /// <summary>
    /// <paramref name="text"/> with a break before any word that would take a line past the
    /// measure. Lines it already has are kept: some tooltips are a list, one thing to a line, and
    /// those breaks mean more than these do.
    /// </summary>
    public static string Wrap(string text)
    {
        if (text.Length <= width)
        {
            return text;
        }

        var wrapped = new StringBuilder(text.Length + 8);
        foreach (var line in text.Split('\n'))
        {
            if (wrapped.Length > 0)
            {
                wrapped.Append('\n');
            }

            Wrap(wrapped, line);
        }

        return wrapped.ToString();
    }

    static void Wrap(StringBuilder wrapped, string line)
    {
        var column = 0;
        foreach (var word in line.Split(' '))
        {
            if (column == 0)
            {
                wrapped.Append(word);
                column = word.Length;
                continue;
            }

            if (column + 1 + word.Length > width)
            {
                wrapped.Append('\n').Append(word);
                column = word.Length;
                continue;
            }

            wrapped.Append(' ').Append(word);
            column += 1 + word.Length;
        }
    }
}
