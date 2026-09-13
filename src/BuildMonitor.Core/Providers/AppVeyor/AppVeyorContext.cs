[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<AppVeyorProject>))]
[JsonSerializable(typeof(AppVeyorHistory))]
[JsonSerializable(typeof(AppVeyorRerun))]
partial class AppVeyorContext : JsonSerializerContext;
