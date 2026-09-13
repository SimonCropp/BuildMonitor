[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitHubRepository>))]
[JsonSerializable(typeof(GitHubWorkflows))]
[JsonSerializable(typeof(GitHubRuns))]
[JsonSerializable(typeof(GitHubUser))]
partial class GitHubContext : JsonSerializerContext;
