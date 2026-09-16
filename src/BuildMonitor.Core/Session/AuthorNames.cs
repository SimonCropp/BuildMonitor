/// <summary>
/// What the author column calls each person: the first name, which is short and usually enough, or
/// the whole name where two different people in the list share a first name, since the first name
/// alone would then point at the wrong one. A bot's first name drops the [bot] GitHub adds to every
/// app's login. The suffix widened the column for every row without saying anything the name did not.
/// </summary>
static class AuthorNames
{
    const string botSuffix = "[bot]";

    public static IReadOnlyDictionary<string, string> Of(IEnumerable<string?> authors)
    {
        var distinct = authors
            .Select(_ => _?.Trim())
            .Where(_ => !string.IsNullOrEmpty(_))
            .Select(_ => _!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shared = distinct
            .GroupBy(FirstName, StringComparer.OrdinalIgnoreCase)
            .Where(_ => _.Count() > 1)
            .Select(_ => _.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return distinct.ToDictionary(
            _ => _,
            _ => shared.Contains(FirstName(_)) ? _ : FirstName(_),
            StringComparer.OrdinalIgnoreCase);
    }

    static string FirstName(string author)
    {
        var space = author.IndexOf(' ');
        var first = space < 0 ? author : author[..space];
        if (first.EndsWith(botSuffix, StringComparison.Ordinal))
        {
            return first[..^botSuffix.Length];
        }

        return first;
    }
}
