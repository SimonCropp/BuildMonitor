class GitLabGraphMergeRequest
{
    public string Iid { get; set; } = "";
    public string? SourceBranch { get; set; }
    // Null where the source project is one the token cannot see.
    public GitLabGraphProject? SourceProject { get; set; }
}
