/// <summary>
/// TeamCity writes 20260101T120000+0000: no separators, and an offset with no colon, which is
/// one format .NET does not read on its own.
/// </summary>
static class TeamCityDate
{
    public static DateTimeOffset? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(text, "yyyyMMdd'T'HHmmsszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            return result;
        }

        // The offset without a colon; insert one and read it as the standard form.
        if (text.Length == 20 &&
            (text[15] == '+' || text[15] == '-'))
        {
            var withColon = $"{text[..18]}:{text[18..]}";
            if (DateTimeOffset.TryParseExact(withColon, "yyyyMMdd'T'HHmmsszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result) ? result : null;
    }
}
