class BitbucketRepository
{
    public string Slug { get; init; } = "";
    public string FullName { get; init; } = "";
    public BitbucketLinks? Links { get; init; }
    public DateTimeOffset? UpdatedOn { get; init; }
    // Bitbucket names it mainbranch, which the snake case policy would spell main_branch.
    [JsonPropertyName("mainbranch")]
    public BitbucketBranch? MainBranch { get; init; }
}