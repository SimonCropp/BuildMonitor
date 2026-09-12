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
        var pipelineWidth = Math.Min(40, builds.Max(_ => _.Pipeline.Length));
        var branchWidth = Math.Min(30, builds.Max(_ => (_.Branch ?? "").Length));
        foreach (var build in builds)
        {
            var run = build.Run.Length == 0 ? "" : $"#{build.Run}";
            builder.AppendLine($"  {Fit(build.Pipeline, pipelineWidth)}  {Fit(build.Branch ?? "", branchWidth)}  {run,-8} {build.Status,-10} {build.Timing,-12} {build.BuildUrl}");
        }

        return builder.ToString();
    }

    static string Fit(string text, int width) =>
        text.Length > width ? $"{text[..(width - 1)]}>" : text.PadRight(width);
}
