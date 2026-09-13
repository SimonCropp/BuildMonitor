[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsProject>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsPipeline>))]
[JsonSerializable(typeof(AzureDevOpsList<AzureDevOpsBuild>))]
partial class AzureDevOpsContext : JsonSerializerContext;
