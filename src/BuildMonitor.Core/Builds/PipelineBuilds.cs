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
/// <param name="Folded">The other branches whose newest run settled any other way, or failed on a
/// branch that is gone, newest first, each with why.</param>
record PipelineBuilds(Build? Head, ImmutableArray<Build> Lanes, ImmutableArray<FoldedBranch> Folded)
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

    /// <summary>
    /// Whether the pipeline is failing: its own run failed. A failed lane is a pull request's or
    /// another branch's, and counting one would keep the tray red for a contributor's broken fork
    /// with nothing wrong on main. It still has its row, and its failure is still announced.
    /// </summary>
    public bool Failing =>
        Head is {Status: BuildStatus.Failed};

    /// <summary>
    /// Whether the pipeline's own run passed.
    /// </summary>
    public bool Passing =>
        Head is {Status: BuildStatus.Succeeded};

    /// <summary>
    /// How many of its runs with a row are running or queued, lanes as well: a pull request being
    /// built is something happening, whichever branch it is on.
    /// </summary>
    public int Running =>
        Shown.Count(_ => _.IsActive);
}
