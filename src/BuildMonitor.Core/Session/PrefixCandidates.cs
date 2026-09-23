/// <summary>
/// The prefixes the context menu offers to group a project by, longest first: every boundary in
/// its name that another project on screen also shares. "Utilities.Logging.Client" beside
/// "Utilities.Logging.Server" and "Utilities.Storage" offers "Utilities.Logging" and "Utilities".
/// <para>
/// A boundary is a separator a project name is built from, or a hump of a camel cased one: a
/// family named TheProjectApi and TheProjectUi has no separator to split on, and a menu that could
/// not offer "TheProject" would leave the options page as the only way to say it.
/// </para>
/// <para>
/// Only prefixes another project shares are offered: a prefix nothing else starts with would make
/// a group of one, which draws as the plain row it already was. A shorter prefix holding the same
/// rows as the last is offered when it ends a separated segment, since a family whose every
/// project on screen is "NServiceBus.Community.*" is still named after "NServiceBus", but not when
/// it only ends a camel hump, where "NService" is the same group under a worse name.
/// </para>
/// </summary>
static class PrefixCandidates
{
    /// <summary>
    /// As many as a menu can carry without burying the items under it. A deeply named project has
    /// a boundary every few characters, and every one of them is a prefix of the one before.
    /// </summary>
    const int most = 4;

    /// <summary>
    /// A single letter groups half an account under an initial, and is as likely a typo as a
    /// choice.
    /// </summary>
    const int shortest = 2;

    static char[] separators = ['.', '-', '_', ' ', '/'];

    public static ImmutableArray<string> Of(string project, IReadOnlyCollection<string> projects, ImmutableArray<string> existing)
    {
        var candidates = ImmutableArray.CreateBuilder<string>();
        // How far the last prefix offered reaches. A camel hump that reaches no further would make
        // the same group under a worse name: "TheProjectApi" beside "TheProjectUi" offers
        // "TheProject", and "The" after it is the same two rows.
        var reach = 1;
        // Backwards, so the longest prefix is offered first: the narrower group is the one the
        // right click was most likely about.
        for (var index = project.Length - 1; index > 0 && candidates.Count < most; index--)
        {
            if (!IsBoundary(project, index))
            {
                continue;
            }

            var separated = separators.Contains(project[index]);

            var candidate = project[..index].TrimEnd(separators);
            if (candidate.Length < shortest ||
                candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase) ||
                existing.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var shared = Sharing(candidate, projects);
            if (shared <= 1)
            {
                continue;
            }

            if (shared == reach &&
                !separated)
            {
                continue;
            }

            reach = shared;
            candidates.Add(candidate);
        }

        return candidates.ToImmutable();
    }

    /// <summary>
    /// How many projects start with the candidate, the one right clicked included: a prefix only
    /// its own row starts with is the group of one this offers nothing for, which is why the first
    /// one offered has to reach past it.
    /// </summary>
    static int Sharing(string candidate, IReadOnlyCollection<string> projects) =>
        projects.Count(_ => _.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the name breaks before this character: a separator, the first capital of a word in
    /// a camel cased name, or the capital that starts a word after an acronym, which is what tells
    /// "ApiGateway" from "APIGateway".
    /// </summary>
    static bool IsBoundary(string project, int index)
    {
        if (separators.Contains(project[index]))
        {
            return true;
        }

        if (!char.IsUpper(project[index]))
        {
            return false;
        }

        if (char.IsLower(project[index - 1]))
        {
            return true;
        }

        return char.IsUpper(project[index - 1]) &&
               index + 1 < project.Length &&
               char.IsLower(project[index + 1]);
    }
}
