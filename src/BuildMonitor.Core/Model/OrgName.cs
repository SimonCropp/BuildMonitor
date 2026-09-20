/// <summary>
/// The account a repository sits under, taken from the repository name rather than carried as a
/// field of its own: every provider that has a level above the repository names the repository for
/// it, as "SimonCropp/Verify". Null where the name has no such level, which is how a provider with
/// nothing above the repository, such as Azure DevOps, ends up offering no org rule.
/// </summary>
static class OrgName
{
    public static string? Of(string repoName)
    {
        var separator = repoName.IndexOf('/');
        if (separator < 0)
        {
            return null;
        }

        var org = repoName[..separator].Trim();
        if (org.Length == 0)
        {
            return null;
        }

        return org;
    }
}
