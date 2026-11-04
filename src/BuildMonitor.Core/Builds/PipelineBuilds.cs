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
/// <param name="Blocker">An older run on the head's branch still running while the head is queued,
/// most likely what it is waiting on, as a deploy waits on the one before it in its concurrency
/// group. Without it the row read "queued" with nothing to say what was running or how long it had
/// left, nor any way to cancel a run stuck in progress that held every later one back.</param>
record PipelineBuilds(Build? Head, ImmutableArray<Build> Lanes, ImmutableArray<FoldedBranch> Folded, Build? Blocker = null)
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
    /// built is something happening, whichever branch it is on, and so is the head's blocker. Counted without walking
    /// <see cref="Shown"/>, whose iterator is an allocation a pipeline, and the counts and the tray
    /// each read this of every pipeline on every rebuild of the screen.
    /// </summary>
    public int Running
    {
        get
        {
            var running = 0;
            if (Head is {IsActive: true})
            {
                running++;
            }

            if (Blocker is not null)
            {
                running++;
            }

            foreach (var lane in Lanes)
            {
                if (lane.IsActive)
                {
                    running++;
                }
            }

            return running;
        }
    }
}
