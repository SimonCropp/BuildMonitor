class BitbucketPipeline
{
    public string Uuid { get; set; } = "";
    public long BuildNumber { get; set; }
    public BitbucketState? State { get; set; }
    public BitbucketPipelineTarget? Target { get; set; }
    public BitbucketCreator? Creator { get; set; }
    public DateTimeOffset? CreatedOn { get; set; }
    public DateTimeOffset? CompletedOn { get; set; }
}