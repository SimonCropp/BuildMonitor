/// <summary>
/// Runs a browser or device sign in off the render loop and reports back into the state.
/// One flow at a time: starting another cancels the first, and a result that arrives for a flow
/// the user left is dropped by the transition's flow id check.
/// </summary>
sealed class SignInCoordinator(SessionHost host, ISecretStore secrets, HttpMessageHandler handler)
{
    CancelSource? current;
    Guid currentFlow;

    public void Start(Connection connection, AuthMethod method, Guid flowId)
    {
        Abandon(currentFlow);
        var cancel = new CancelSource();
        current = cancel;
        currentFlow = flowId;
        _ = Task.Run(() => Run(connection, method, flowId, cancel.Token), Cancel.None);
    }

    public void Abandon(Guid flowId)
    {
        if (flowId == currentFlow &&
            current is { } running)
        {
            running.Cancel();
            current = null;
        }
    }

    async Task Run(Connection connection, AuthMethod method, Guid flowId, Cancel cancel)
    {
        var descriptor = ProviderDescriptors.Get(connection.ProviderId);
        var client = OAuthClients.For(connection);
        if (client is null)
        {
            Fail(flowId, $"No OAuth application is registered for {descriptor.Name}. Enter an application id, or use a {descriptor.TokenLabel.ToLowerInvariant()}.");
            return;
        }

        if (method == AuthMethod.Browser &&
            client.ProviderId == "github" &&
            client.ClientSecret is null &&
            connection.ClientId is null)
        {
            Fail(flowId, "GitHub's browser sign in needs a client secret. Use the device flow instead.");
            return;
        }

        try
        {
            var result = method == AuthMethod.Device
                ? await OAuthFlows.Device(
                    client,
                    (code, url) => host.Mutate(_ => MonitorSession.SignInProgress(_, flowId, code, url)),
                    handler,
                    cancel)
                : await OAuthFlows.Browser(client, LinkLauncher.OpenUrl, handler, connection.CallbackPort ?? 0, cancel);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            if (!result.Ok)
            {
                Fail(flowId, result.Error ?? "The sign in failed.");
                return;
            }

            secrets.Write(SecretKeys.Token(connection.Id), result.AccessToken!);
            if (result.RefreshToken is not null)
            {
                secrets.Write(SecretKeys.Refresh(connection.Id), result.RefreshToken);
            }
            else
            {
                secrets.Delete(SecretKeys.Refresh(connection.Id));
            }

            var who = await WhoAmI(connection, result.AccessToken!, cancel);
            host.Mutate(_ => MonitorSession.SignInCompleted(_, flowId, who));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Sign in failed");
            Fail(flowId, exception.Message);
        }
    }

    async Task<string?> WhoAmI(Connection connection, string token, Cancel cancel)
    {
        try
        {
            var test = await Providers.Get(connection.ProviderId).Test(Providers.Context(connection, token, handler), cancel);
            const string prefix = "Signed in as ";
            return test.Message.StartsWith(prefix, StringComparison.Ordinal) ? test.Message[prefix.Length..] : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or AuthException or RateLimitException)
        {
            return null;
        }
    }

    void Fail(Guid flowId, string error) =>
        host.Mutate(_ => MonitorSession.SignInFailed(_, flowId, error));
}
