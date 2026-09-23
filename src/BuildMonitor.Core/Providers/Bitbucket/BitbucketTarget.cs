record BitbucketTarget(
    string Type,
    string? RefType,
    string? RefName,
    BitbucketTargetCommit Commit,
    string? Source = null,
    string? Destination = null,
    BitbucketTargetCommit? DestinationCommit = null,
    // Bitbucket names it pullrequest, which the snake case policy spells pull_request.
    [property: JsonPropertyName("pullrequest")]
    BitbucketTargetPullRequest? PullRequest = null);
