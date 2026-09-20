/// <summary>
/// A thing that produces builds: a workflow, a job, a build configuration, a project.
/// </summary>
/// <param name="Url">The pipeline's own page on the CI service, which every build URL is composed
/// under and which the row's provider icon opens.</param>
/// <param name="RepoUrl">The source repository the pipeline builds, where discovery knows it.
/// Carried on the pipeline because that is where the API says it: a fetch is grouped by repository
/// name and has nothing else to derive the address from, and a provider that composed it per build
/// would have to guess the host, which is what pointed GitHub Enterprise rows at github.com. Null
/// where the service does not report it, and then the row's name is not a link.</param>
record Pipeline(string Id, string Name, string RepoName, string? Group, string Url, string? RepoUrl = null);
