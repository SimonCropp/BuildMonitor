/// <summary>
/// The token endpoint's answer, success or error, in the shape every provider here uses.
/// </summary>
class TokenResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int? ExpiresIn { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public int? Interval { get; set; }
}

class DeviceCodeResponse
{
    public string? DeviceCode { get; set; }
    public string? UserCode { get; set; }
    public string? VerificationUri { get; set; }
    public string? VerificationUriComplete { get; set; }
    public int? ExpiresIn { get; set; }
    public int? Interval { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(DeviceCodeResponse))]
partial class OAuthContext : JsonSerializerContext;
