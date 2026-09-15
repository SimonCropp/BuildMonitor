class TravisRepository
{
    public long Id { get; set; }
    public string Slug { get; set; } = "";
    // Only when asked for with include=repository.last_started_build, as the probe does.
    public TravisBuild? LastStartedBuild { get; set; }
}