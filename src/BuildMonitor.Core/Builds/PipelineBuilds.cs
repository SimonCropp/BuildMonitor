/// <summary>
/// One pipeline's runs as the rows show them: its own run, and the newest run of each of its other
/// branches, split into the lanes that get a row and the ones that do not. Kept together so a pull
/// request's run cannot stand in for the pipeline: while a pipeline's row was simply its newest run,
/// a green pull request hid a red main, and a red one painted main red.
/// </summary>
/// <param name="Head">The pipeline's own run: the newest on its default branch, or the newest on any
/// branch where the default is not known or no run on it is held. Null only where a deferral hid it.</param>
/// <param name="Lanes">The other branches whose newest run is running, queued or failed, most urgent
/// first.</param>
/// <param name="Folded">The other branches whose newest run settled any other way, newest first.</param>
record PipelineBuilds(Build? Head, ImmutableArray<Build> Lanes, ImmutableArray<Build> Folded)
{
    /// <summary>
    /// The runs that get a row, the head first. Built on every read rather than held, since a copy
    /// made with <c>with</c> would keep the list built for the original.
    /// </summary>
    public IEnumerable<Build> Shown
    {
        get
        {
            if (Head is not null)
            {
                yield return Head;
            }

            foreach (var lane in Lanes)
            {
                yield return lane;
            }
        }
    }
}
