class BitbucketPipelineTarget
{
    public string? Type { get; set; }
    public string? RefType { get; set; }
    public string? RefName { get; set; }
    public BitbucketCommit? Commit { get; set; }
    public BitbucketPullRequest? PullRequest { get; set; }
}