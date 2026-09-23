class BitbucketPullRequest
{
    public long? Id { get; set; }
    // OPEN, MERGED, DECLINED or SUPERSEDED. Only a pull request asked for itself says so; a
    // pipeline's target names only its id.
    public string? State { get; set; }
}