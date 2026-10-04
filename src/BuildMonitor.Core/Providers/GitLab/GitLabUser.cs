class GitLabUser
{
    public string Username { get; init; } = "";
    // Sent only to an administrator.
    public bool IsAdmin { get; init; }
}