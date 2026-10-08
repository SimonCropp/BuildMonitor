class CircleCiJob
{
    public string Name { get; set; } = "";
    // Missing for an approval, and for a job that has not started.
    public long? JobNumber { get; set; }
    public string Status { get; set; } = "";
}
