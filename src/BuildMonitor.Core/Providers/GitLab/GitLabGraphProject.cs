class GitLabGraphProject
{
    // A global id: gid://gitlab/Project/77.
    public string Id { get; set; } = "";
    public GitLabGraphPipelines? Pipelines { get; set; }
    // As a merge request's source project: where its branch is, a fork's for one from a fork.
    public string? FullPath { get; set; }
    public string? WebUrl { get; set; }
}