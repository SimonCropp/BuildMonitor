class AppVeyorProject
{
    public string AccountName { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? RepositoryType { get; set; }
    public string? RepositoryName { get; set; }
    // The projects list carries each project's latest build.
    public List<AppVeyorBuild> Builds { get; set; } = [];
}