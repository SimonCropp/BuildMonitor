[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitLabProject>))]
[JsonSerializable(typeof(List<GitLabPipeline>))]
[JsonSerializable(typeof(GitLabPipeline))]
[JsonSerializable(typeof(List<GitLabJob>))]
[JsonSerializable(typeof(GitLabUser))]
partial class GitLabContext : JsonSerializerContext;