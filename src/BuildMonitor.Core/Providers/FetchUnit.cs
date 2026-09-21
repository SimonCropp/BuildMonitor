/// <summary>
/// What one fetch covers, and so what the poller schedules as one group. Octopus sizes one listing
/// by the number of pipelines asked for, so fetching a subset would change the request and shrink
/// the window; the whole connection is one group for it.
/// </summary>
public enum FetchUnit
{
    // A request per pipeline: AppVeyor, Bitbucket, GoCD, Jenkins, Travis.
    Pipeline,

    // A request covers every pipeline sharing a RepoName: a GitHub repository, an Azure DevOps project.
    Repository,

    // A request covers every pipeline of the connection: GitLab, whose GraphQL request covers fifty
    // projects, and Octopus.
    Connection,

    // A request covers every pipeline sharing a Group: a TeamCity project, by id, since its name
    // need not be unique.
    Group
}
