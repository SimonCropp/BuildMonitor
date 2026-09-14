class GitHubRepository
{
    public string FullName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public bool Archived { get; set; }
    public bool Disabled { get; set; }
    public bool Fork { get; set; }
    public DateTimeOffset? PushedAt { get; set; }
}