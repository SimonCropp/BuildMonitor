/// <summary>
/// The status verb's text.
/// </summary>
static class StatusPrinter
{
    public static string Render(SummaryDto summary, IReadOnlyList<BuildDto> builds)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{summary.Pipelines} pipelines, {summary.Failing} failing, {summary.Running} running. {summary.Status}");
        foreach (var connection in summary.ConnectionHealth)
        {
            var error = connection.Error is null ? "" : $" ({connection.Error})";
            builder.AppendLine($"  {connection.Name}: {connection.Provider}, {connection.Health}{error}");
        }

        if (builds.Count == 0)
        {
            return builder.ToString();
        }

        builder.AppendLine();
        // The repository leads, as it does in the window: a pipeline name such as a shared
        // workflow repeats across repositories and tells the lines apart by nothing.
        var repoWidth = Math.Min(30, builds.Max(_ => BuildExtensions.ShortRepoName(_.Repo).Length));
        var pipelineWidth = Math.Min(40, builds.Max(_ => _.Pipeline.Length));
        var branchWidth = Math.Min(30, builds.Max(_ => (_.Branch ?? "").Length));
        foreach (var build in builds)
        {
            var run = build.Run.Length == 0 ? "" : $"#{build.Run}";
            var repo = Fit(BuildExtensions.ShortRepoName(build.Repo), repoWidth);
            var pipeline = Fit(build.Pipeline, pipelineWidth);
            var branch = Fit(build.Branch ?? "", branchWidth);
            builder.AppendLine($"  {repo}  {pipeline}  {branch}  {run,-8} {build.Status,-10} {build.Timing,-12} {build.BuildUrl}");
        }

        return builder.ToString();
    }

    static string Fit(string text, int width)
    {
        if (text.Length > width)
        {
            return $"{text[..(width - 1)]}>";
        }

        return text.PadRight(width);
    }
}
