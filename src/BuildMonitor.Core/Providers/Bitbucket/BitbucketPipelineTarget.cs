class BitbucketPipelineTarget
{
    public string? Type { get; set; }
    public string? RefType { get; set; }
    public string? RefName { get; set; }
    public BitbucketCommit? Commit { get; set; }
    // Bitbucket names it pullrequest, which the snake case policy spells pull_request, so no
    // pipeline carried its pull request.
    [JsonPropertyName("pullrequest")]
    public BitbucketPullRequest? PullRequest { get; set; }
}