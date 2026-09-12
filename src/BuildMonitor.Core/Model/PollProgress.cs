/// <summary>
/// How far a poll has got, in whatever unit the provider walks: repositories for GitHub,
/// projects for Azure DevOps. Shown on the connection's header row while it polls.
/// </summary>
readonly record struct PollProgress(int Done, int Total);
