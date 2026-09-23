class TravisCommit
{
    public string? Sha { get; set; }
    public string? Message { get; set; }
    public TravisAuthor? Author { get; set; }
    // The commit's compare, commit or pull request page under its repository, as GitHub's webhook
    // gave it. The one address Travis names on the repository's host.
    public string? CompareUrl { get; set; }
}