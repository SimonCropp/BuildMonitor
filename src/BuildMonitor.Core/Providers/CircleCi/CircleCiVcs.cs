class CircleCiVcs
{
    public string? TargetRepositoryUrl { get; set; }
    // One of the two: a pipeline is for a branch or for a tag.
    public string? Branch { get; set; }
    public string? Tag { get; set; }
    public string? Revision { get; set; }

    // The pull request's number, which the documentation gives as text.
    [JsonConverter(typeof(TextOrNothingConverter))]
    public string? ReviewId { get; set; }
    public string? ReviewUrl { get; set; }
    public CircleCiCommit? Commit { get; set; }
}
