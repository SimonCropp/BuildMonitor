class TravisRepository
{
    public long Id { get; set; }
    public string Slug { get; set; } = "";
    // Only when asked for with include=repository.last_started_build, as the probe does.
    public TravisBuild? LastStartedBuild { get; set; }
    // In the listing discovery reads already, which sorts by its last build.
    public TravisBranch? DefaultBranch { get; set; }
    // Where the source is: GithubRepository, BitbucketRepository, GitlabRepository or
    // AssemblaRepository. travis-ci.com builds from all four.
    public string? VcsType { get; set; }
}