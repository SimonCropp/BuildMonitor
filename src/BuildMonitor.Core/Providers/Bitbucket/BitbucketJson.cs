class BitbucketPage
{
    public List<BitbucketRepository> Values { get; set; } = [];
    public string? Next { get; set; }
}

class BitbucketRepository
{
    public string Slug { get; set; } = "";
    public string FullName { get; set; } = "";
    public BitbucketLinks? Links { get; set; }
}

class BitbucketLinks
{
    public BitbucketLink? Html { get; set; }
}

class BitbucketLink
{
    public string Href { get; set; } = "";
}

class BitbucketPipelinePage
{
    public List<BitbucketPipeline> Values { get; set; } = [];
}

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

class BitbucketState
{
    public string? Name { get; set; }
    public BitbucketResult? Result { get; set; }
}

class BitbucketResult
{
    public string? Name { get; set; }
}

class BitbucketPipelineTarget
{
    public string? Type { get; set; }
    public string? RefType { get; set; }
    public string? RefName { get; set; }
    public BitbucketCommit? Commit { get; set; }
    public BitbucketPullRequest? PullRequest { get; set; }
}

class BitbucketCommit
{
    public string? Hash { get; set; }
}

class BitbucketPullRequest
{
    public long? Id { get; set; }
}

class BitbucketCreator
{
    public string? DisplayName { get; set; }
}

class BitbucketWorkspace
{
    public string Name { get; set; } = "";
}

record BitbucketTrigger(BitbucketTarget Target);

record BitbucketTarget(string Type, string? RefType, string? RefName, BitbucketTargetCommit Commit);

record BitbucketTargetCommit(string Type, string Hash);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BitbucketPage))]
[JsonSerializable(typeof(BitbucketPipelinePage))]
[JsonSerializable(typeof(BitbucketWorkspace))]
[JsonSerializable(typeof(BitbucketTrigger))]
partial class BitbucketContext : JsonSerializerContext;
