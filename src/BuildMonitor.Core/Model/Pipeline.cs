/// <summary>
/// A thing that produces builds: a workflow, a job, a build configuration, a project.
/// </summary>
record Pipeline(string Id, string Name, string RepoName, string? Group, string Url);
