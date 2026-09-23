[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsProject>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsPipeline>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsBuild>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsArtifact>))]
[JsonSerializable(typeof(AzureDevOpsTimeline))]
[JsonSerializable(typeof(AzureDevOpsPullRequest))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsRef>))]
partial class AzureDevOpsContext : JsonSerializerContext;
