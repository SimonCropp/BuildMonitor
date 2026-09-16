class GitLabUser
{
    public string Username { get; set; } = "";
    // Sent only to an administrator.
    public bool IsAdmin { get; set; }
}