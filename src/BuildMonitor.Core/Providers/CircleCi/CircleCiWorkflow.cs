class CircleCiWorkflow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // success, running, not_run, failed, error, failing, on_hold, canceled or unauthorized.
    public string Status { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
}
