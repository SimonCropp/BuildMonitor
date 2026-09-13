/// <summary>
/// The three OAuth exchanges: authorization code through the system browser and a loopback
/// redirect, the device flow, and a refresh. All three end at the same token endpoint.
/// </summary>
static class OAuthFlows
{
    static readonly TimeSpan browserTimeout = TimeSpan.FromMinutes(5);

    public static async Task<AuthResult> Browser(
        OAuthClient client,
        Action<string> openBrowser,
        HttpMessageHandler handler,
        int fixedPort,
        Cancel cancel)
    {
        using var listener = new LoopbackListener(fixedPort);
        var redirect = client.RedirectUri(listener.Port);
        var state = Pkce.Base64Url(RandomNumberGenerator.GetBytes(16));
        var (verifier, challenge) = Pkce.Create();
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = client.Scope,
            ["state"] = state
        };
        if (client.Pkce)
        {
            parameters["code_challenge"] = challenge;
            parameters["code_challenge_method"] = "S256";
        }

        if (client.ExtraAuthorizeParameters is not null)
        {
            foreach (var (name, value) in client.ExtraAuthorizeParameters)
            {
                parameters[name] = value;
            }
        }

        openBrowser($"{client.AuthorizeUrl}?{Query(parameters)}");

        using var timeout = CancelSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(browserTimeout);
        IReadOnlyDictionary<string, string> callback;
        try
        {
            callback = await listener.WaitForCallback(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return AuthResult.Failed("The browser did not come back within five minutes.");
        }

        if (callback.TryGetValue("error", out var error))
        {
            callback.TryGetValue("error_description", out var description);
            return AuthResult.Failed(description ?? error);
        }

        if (!callback.TryGetValue("state", out var returned) ||
            returned != state)
        {
            return AuthResult.Failed("The response did not match the request.");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["code"] = callback["code"],
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code"
        };
        if (client.Pkce)
        {
            form["code_verifier"] = verifier;
        }

        if (client.ClientSecret is not null)
        {
            form["client_secret"] = client.ClientSecret;
        }

        var token = await Token(client, form, handler, cancel);
        return ToResult(token);
    }

    public static async Task<AuthResult> Device(
        OAuthClient client,
        Action<string, string> show,
        HttpMessageHandler handler,
        Cancel cancel,
        Func<TimeSpan, Cancel, Task>? delay = null)
    {
        if (client.DeviceCodeUrl is null)
        {
            return AuthResult.Failed("This provider has no device flow.");
        }

        delay ??= Task.Delay;
        using var http = new HttpClient(handler, disposeHandler: false);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        using var start = await http.PostAsync(
            client.DeviceCodeUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = client.ClientId,
                ["scope"] = client.Scope
            }),
            cancel);
        var device = await Read(start, OAuthContext.Default.DeviceCodeResponse, cancel);
        if (device.DeviceCode is null ||
            device.UserCode is null)
        {
            return AuthResult.Failed(device.ErrorDescription ?? device.Error ?? "The device code request failed.");
        }

        show(device.UserCode, device.VerificationUriComplete ?? device.VerificationUri ?? "");
        var interval = TimeSpan.FromSeconds(Math.Max(1, device.Interval ?? 5));
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(device.ExpiresIn ?? 900);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await delay(interval, cancel);
            var token = await Token(
                client,
                new()
                {
                    ["client_id"] = client.ClientId,
                    ["device_code"] = device.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                },
                handler,
                cancel);
            switch (token.Error)
            {
                case null when token.AccessToken is not null:
                    return ToResult(token);
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                default:
                    return AuthResult.Failed(token.ErrorDescription ?? token.Error ?? "The sign in failed.");
            }
        }

        return AuthResult.Failed("The code expired before it was entered.");
    }

    public static Task<TokenResponse> Refresh(OAuthClient client, string refreshToken, HttpMessageHandler handler, Cancel cancel)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = client.Scope
        };
        if (client.ClientSecret is not null)
        {
            form["client_secret"] = client.ClientSecret;
        }

        return Token(client, form, handler, cancel);
    }

    static async Task<TokenResponse> Token(OAuthClient client, Dictionary<string, string> form, HttpMessageHandler handler, Cancel cancel)
    {
        using var http = new HttpClient(handler, disposeHandler: false);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        using var response = await http.PostAsync(client.TokenUrl, new FormUrlEncodedContent(form), cancel);
        return await Read(response, OAuthContext.Default.TokenResponse, cancel);
    }

    /// <summary>
    /// Token endpoints answer errors with a 400 and a JSON body that says which, so the status
    /// is not the signal; an unreadable body is.
    /// </summary>
    static async Task<T> Read<T>(HttpResponseMessage response, JsonTypeInfo<T> info, Cancel cancel)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        try
        {
            return JsonSerializer.Deserialize(bytes, info) ??
                   throw new HttpRequestException($"Empty response from {response.RequestMessage?.RequestUri}");
        }
        catch (JsonException)
        {
            throw new HttpRequestException($"{(int) response.StatusCode} from {response.RequestMessage?.RequestUri}: {Encoding.UTF8.GetString(bytes)}");
        }
    }

    static AuthResult ToResult(TokenResponse token)
    {
        if (token.AccessToken is null)
        {
            return AuthResult.Failed(token.ErrorDescription ?? token.Error ?? "No token was returned.");
        }

        return AuthResult.Success(token.AccessToken, token.RefreshToken);
    }

    static string Query(Dictionary<string, string> parameters) =>
        string.Join('&', parameters.Select(_ => $"{Uri.EscapeDataString(_.Key)}={Uri.EscapeDataString(_.Value)}"));
}
