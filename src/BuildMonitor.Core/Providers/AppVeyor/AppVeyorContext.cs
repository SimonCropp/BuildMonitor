[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Converters = [typeof(PooledStringConverter)])]
[JsonSerializable(typeof(List<AppVeyorProject>))]
[JsonSerializable(typeof(AppVeyorHistory))]
[JsonSerializable(typeof(AppVeyorBuildDetail))]
[JsonSerializable(typeof(List<AppVeyorArtifact>))]
[JsonSerializable(typeof(AppVeyorRerun))]
partial class AppVeyorContext : JsonSerializerContext;
