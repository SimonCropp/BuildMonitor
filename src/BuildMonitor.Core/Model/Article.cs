/// <summary>
/// The indefinite article for a noun a provider names, so a sentence built around
/// <see cref="ProviderDescriptor.TokenLabel"/> reads "an API token" and "a personal access token"
/// without every provider spelling its own out.
/// </summary>
static class Article
{
    public static string For(string noun)
    {
        if ("AEIOUaeiou".Contains(noun[0]))
        {
            return "an";
        }

        return "a";
    }
}
