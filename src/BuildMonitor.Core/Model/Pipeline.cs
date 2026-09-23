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
/// <param name="ProjectGroup">What the service itself files the pipeline under, where it has such
/// a thing: an Octopus project group. Passing builds of one of these share a group, ahead of the
/// repository name, so a family of projects deployed together reads as one row without anyone
/// having to name a prefix. Null where the service has no such grouping, which is every provider
/// but Octopus Deploy.</param>
/// <param name="DefaultBranch">The branch the pipeline's own runs are on, where discovery can say:
/// the repository's default branch, in the form the provider writes <see cref="Build.Branch"/>.
/// Without it a pull request's run was as much the pipeline as a push to main was. Null where the
/// service does not say at discovery. Where what it says can be stale, as AppVeyor's setting is,
/// the provider checks it against the runs it fetches before any build carries it.</param>
record Pipeline(string Id, string Name, string RepoName, string? Group, string Url, string? RepoUrl = null, string? ProjectGroup = null, string? DefaultBranch = null);
