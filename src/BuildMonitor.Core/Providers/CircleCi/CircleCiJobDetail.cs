/// <summary>
/// A job as API v1.1 describes it, which is the only place its steps are listed.
/// </summary>
class CircleCiJobDetail
{
    public List<CircleCiStep> Steps { get; set; } = [];
}
