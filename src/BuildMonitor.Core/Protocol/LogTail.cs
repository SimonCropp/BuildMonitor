/// <summary>
/// The end of a build log, small enough to answer with. A CI log runs to megabytes, which the
/// answer would carry as one base64 line and no assistant could read, so only the end is kept.
/// Each section is tailed on its own rather than the text as a whole, because a run whose first
/// job failed and whose last one then failed too would otherwise answer with nothing but the
/// last one's ending.
/// </summary>
static class LogTail
{
    /// <summary>
    /// What a caller that asks for no particular size gets: enough for a stack trace and the
    /// build output around it.
    /// </summary>
    public const int DefaultLines = 200;

    /// <summary>
    /// The last <paramref name="maxLines"/> lines of each section, under a line saying how many
    /// of that section's were dropped, so a reader knows the log did not start there. A size
    /// below one is read as one, because a section headed by nothing at all says less than the
    /// header and the count do.
    /// </summary>
    public static string Take(string log, int maxLines)
    {
        var limit = Math.Max(maxLines, 1);
        return string.Join("\n\n", Split(log).Select(_ => Tail(log, _, limit)));
    }

    /// <summary>
    /// The lines grouped by the section headers <c>ProviderBase.Sections</c> writes, with the
    /// blank line between two sections dropped so joining them back cannot double it up. A log
    /// with no headers, which is what a service that keeps one log a build returns, is one
    /// section. Lines are ranges into the log rather than strings, because a log runs to
    /// megabytes and all but its tail are dropped.
    /// </summary>
    static List<List<Range>> Split(string log)
    {
        var sections = new List<List<Range>>();
        var current = new List<Range>();
        var start = 0;
        while (true)
        {
            var newline = log.IndexOf('\n', start);
            var end = newline < 0 ? log.Length : newline;
            var line = start..end;
            if (IsHeader(log.AsSpan(line)) &&
                current.Count > 0)
            {
                sections.Add(current);
                current = [];
            }

            current.Add(line);
            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }

        sections.Add(current);
        foreach (var section in sections)
        {
            while (section.Count > 0 &&
                   log.AsSpan(section[^1]).IsWhiteSpace())
            {
                section.RemoveAt(section.Count - 1);
            }
        }

        return sections;
    }

    static string Tail(string log, List<Range> section, int maxLines)
    {
        var header = section.Count > 0 && IsHeader(log.AsSpan(section[0])) ? 1 : 0;
        var dropped = section.Count - header - maxLines;
        if (dropped <= 0)
        {
            return string.Join("\n", section.Select(_ => log[_]));
        }

        return string.Join(
            "\n",
            section
                .Take(header)
                .Select(_ => log[_])
                .Append($"... {dropped} earlier {(dropped == 1 ? "line" : "lines")} dropped")
                .Concat(section.Skip(section.Count - maxLines).Select(_ => log[_])));
    }

    /// <summary>
    /// A line a log of its own was written under. A job that printed one itself would split its
    /// log in two here, which costs nothing but a heading the tail is measured against.
    /// </summary>
    static bool IsHeader(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimEnd();
        return trimmed.StartsWith("==> ", StringComparison.Ordinal) &&
               trimmed.EndsWith(" <==", StringComparison.Ordinal);
    }
}
