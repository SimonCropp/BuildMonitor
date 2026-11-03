// An issue search result, which for a pull request names its repository only by API address.
class GitHubIssue
{
    public long Number { get; init; }
    public string RepositoryUrl { get; init; } = "";
}
