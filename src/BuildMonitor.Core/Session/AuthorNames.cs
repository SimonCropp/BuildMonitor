/// <summary>
/// What the author column calls each person: the first name, which is short and usually enough, or
/// the whole name where two different people in the list share a first name, since the first name
/// alone would then point at the wrong one.
/// <para>
/// An app is a robot rather than a name. Its login says nothing about who broke the build, only
/// that nobody did, and every one of them is some flavour of "-bot" that widened the column to say
/// it. Where two apps are on screen at once the robot alone would point at the wrong one, so each
/// keeps its name behind the mark, on the same rule as two people sharing a first name.
/// </para>
/// </summary>
static class AuthorNames
{
    public static IReadOnlyDictionary<string, string> Of(IEnumerable<string?> authors)
    {
        var distinct = authors
            .Select(_ => _?.Trim())
            .Where(_ => !string.IsNullOrEmpty(_))
            .Select(_ => _!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shared = distinct
            .GroupBy(Short, StringComparer.OrdinalIgnoreCase)
            .Where(_ => _.Count() > 1)
            .Select(_ => _.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return distinct.ToDictionary(
            _ => _,
            _ => shared.Contains(Short(_)) ? Full(_) : Short(_),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What the column shows where nothing else on it collides.
    /// </summary>
    static string Short(string author)
    {
        if (Bots.NameOf(author) is not null)
        {
            return Bots.Mark;
        }

        var space = author.IndexOf(' ');
        if (space < 0)
        {
            return author;
        }

        return author[..space];
    }

    /// <summary>
    /// What it shows instead where two authors share a <see cref="Short"/>: enough to tell them
    /// apart, which for an app is the mark it would have had and the name behind it.
    /// </summary>
    static string Full(string author)
    {
        if (Bots.NameOf(author) is { } name)
        {
            return $"{Bots.Mark} {name}";
        }

        return author;
    }
}
