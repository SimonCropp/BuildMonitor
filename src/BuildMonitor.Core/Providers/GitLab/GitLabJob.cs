class GitLabJob
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Stage { get; set; }
    public bool AllowFailure { get; set; }
}