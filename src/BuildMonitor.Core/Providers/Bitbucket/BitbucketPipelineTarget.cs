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
    // A pull request pipeline's branches, which it names in place of a ref: the one it came from
    // and the one it targets.
    public string? Source { get; set; }
    public string? Destination { get; set; }
}