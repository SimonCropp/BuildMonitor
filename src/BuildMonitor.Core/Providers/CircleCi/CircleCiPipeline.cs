/// <summary>
/// One run of a project's configuration. It has no status of its own beyond whether it could be
/// set up: how it went is what its workflows say.
/// </summary>
class CircleCiPipeline
{
    public string Id { get; set; } = "";
    public long Number { get; set; }
    public string ProjectSlug { get; set; } = "";
    // created, errored, setup-pending, setup or pending.
    public string State { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public CircleCiTrigger? Trigger { get; set; }
    public CircleCiVcs? Vcs { get; set; }
}
