[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitLabProject>))]
[JsonSerializable(typeof(List<GitLabPipeline>))]
[JsonSerializable(typeof(GitLabPipeline))]
[JsonSerializable(typeof(GitLabUser))]
partial class GitLabContext : JsonSerializerContext;