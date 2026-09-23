class BitbucketRepository
{
    public string Slug { get; set; } = "";
    public string FullName { get; set; } = "";
    public BitbucketLinks? Links { get; set; }
    public DateTimeOffset? UpdatedOn { get; set; }
    // Bitbucket names it mainbranch, which the snake case policy would spell main_branch.
    [JsonPropertyName("mainbranch")]
    public BitbucketBranch? MainBranch { get; set; }
}