class GitLabGraphProject
{
    // A global id: gid://gitlab/Project/77.
    public string Id { get; set; } = "";
    public GitLabGraphPipelines? Pipelines { get; set; }
}