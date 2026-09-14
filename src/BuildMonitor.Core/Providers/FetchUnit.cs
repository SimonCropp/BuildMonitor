/// <summary>
/// What one fetch covers, and so what the poller schedules as one group. TeamCity and Octopus
/// size one listing by the number of pipelines asked for, so fetching a subset would change the
/// request and shrink the window; the whole connection is one group for them.
/// </summary>
enum FetchUnit
{
    // A request per pipeline: AppVeyor, Bitbucket, GitLab, GoCD, Jenkins, Travis.
    Pipeline,

    // A request covers every pipeline sharing a RepoName: a GitHub repository, an Azure DevOps project.
    Repository,

    // A request covers every pipeline of the connection.
    Connection
}
