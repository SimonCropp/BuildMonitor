[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BitbucketPage))]
[JsonSerializable(typeof(BitbucketPipelinePage))]
[JsonSerializable(typeof(BitbucketWorkspace))]
[JsonSerializable(typeof(BitbucketTrigger))]
partial class BitbucketContext : JsonSerializerContext;
