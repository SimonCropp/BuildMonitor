/// <summary>
/// Orders the version directories of the dotnet tool store the way NuGet orders versions: by the
/// numbers, then a release above its own prereleases, then the prerelease labels one at a time,
/// with numeric ones compared by value. Compared as text, 0.1.0-beta.10 sorts below 0.1.0-beta.9.
/// </summary>
static class PackageVersion
{
    public static int Compare(string left, string right)
    {
        var (leftNumbers, leftLabels) = Split(left);
        var (rightNumbers, rightLabels) = Split(right);
        var numbers = CompareNumbers(leftNumbers, rightNumbers);
        if (numbers != 0)
        {
            return numbers;
        }

        if (leftLabels is null)
        {
            if (rightLabels is null)
            {
                return 0;
            }

            return 1;
        }

        if (rightLabels is null)
        {
            return -1;
        }

        return CompareLabels(leftLabels, rightLabels);
    }

    static (string[] Numbers, string[]? Labels) Split(string version)
    {
        // Build metadata takes no part in the order, and the store leaves it out anyway.
        var plus = version.IndexOf('+');
        if (plus >= 0)
        {
            version = version[..plus];
        }

        var dash = version.IndexOf('-');
        if (dash < 0)
        {
            return (version.Split('.'), null);
        }

        return (version[..dash].Split('.'), version[(dash + 1)..].Split('.'));
    }

    /// <summary>
    /// A missing number is a zero, so 1.0 is 1.0.0, and 1.0.0.1 is above both.
    /// </summary>
    static int CompareNumbers(string[] left, string[] right)
    {
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var compared = CompareIdentifier(left.ElementAtOrDefault(index) ?? "0", right.ElementAtOrDefault(index) ?? "0");
            if (compared != 0)
            {
                return compared;
            }
        }

        return 0;
    }

    /// <summary>
    /// Labels that another set starts with sort below it, so beta is below beta.1.
    /// </summary>
    static int CompareLabels(string[] left, string[] right)
    {
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var compared = CompareIdentifier(left[index], right[index]);
            if (compared != 0)
            {
                return compared;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// A numeric identifier sorts below a named one, so alpha.1 is below alpha.beta.
    /// </summary>
    static int CompareIdentifier(string left, string right)
    {
        var leftIsNumber = ulong.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumber = ulong.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        if (leftIsNumber && rightIsNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (leftIsNumber)
        {
            return -1;
        }

        if (rightIsNumber)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
