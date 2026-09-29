/// <summary>
/// What the header, the tray and the MCP summary count. Pipelines rather than rows, since a lane
/// counted as a pipeline of its own had the header saying nine pipelines of six, and failing as
/// <see cref="PipelineBuilds.Failing"/> says, so every surface agrees on what a red pipeline is.
/// </summary>
record BuildCounts(int Pipelines, int Failing, int Running)
{
    public static BuildCounts Of(ImmutableArray<PipelineBuilds> pipelines)
    {
        var failing = 0;
        var running = 0;
        foreach (var pipeline in pipelines)
        {
            if (pipeline.Failing)
            {
                failing++;
            }

            running += pipeline.Running;
        }

        return new(pipelines.Length, failing, running);
    }
}
