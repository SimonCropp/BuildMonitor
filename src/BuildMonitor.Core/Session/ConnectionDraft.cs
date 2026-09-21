/// <summary>
/// Reads the connection editor's fields back into a <see cref="Connection"/>. Kept apart from
/// the transitions because it is the one place a form's strings are interpreted, and its errors
/// are the messages the form shows.
/// </summary>
static class ConnectionDraft
{
    public static AuthMethod Method(ConnectionFormState form)
    {
        if (Enum.TryParse<AuthMethod>(form.Value(FormFields.Auth), out var method))
        {
            return method;
        }

        return AuthMethod.Token;
    }

    /// <summary>
    /// The connection as currently described, without checking it is complete. Enough to start a
    /// sign in or a test.
    /// </summary>
    public static Connection Build(ConnectionFormState form)
    {
        var descriptor = form.Descriptor;
        var scope = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var field in descriptor.Scopes)
        {
            var value = form.Value(FormFields.Scope(field.Id)).Trim();
            if (value.Length > 0)
            {
                scope[field.Id] = value;
            }
        }

        var server = ServerAddress.Normalize(form.Value(FormFields.Server));
        var user = form.Value(FormFields.User).Trim();
        var clientId = form.Value(FormFields.ClientId).Trim();
        var callbackPort = int.TryParse(form.Value(FormFields.CallbackPort).Trim(), out var parsedPort) ? parsedPort : (int?) null;
        return new()
        {
            Id = form.ConnectionId,
            ProviderId = descriptor.Id,
            Name = form.Value(FormFields.Name).Trim() is { Length: > 0 } name ? name : descriptor.Name,
            Server = server.Length == 0 ? null : server,
            Scope = scope.ToImmutable(),
            Auth = Method(form),
            User = user.Length == 0 ? null : user,
            ClientId = clientId.Length == 0 ? null : clientId,
            CallbackPort = callbackPort
        };
    }

    public static string? Token(ConnectionFormState form)
    {
        var token = form.Value(FormFields.Token).Trim();
        if (token.Length == 0)
        {
            return null;
        }

        return token;
    }

    /// <summary>
    /// The first thing wrong with the draft, or null when it can be saved.
    /// </summary>
    public static string? Validate(ConnectionFormState form)
    {
        var descriptor = form.Descriptor;
        if (descriptor is {SelfHosted: true, DefaultServer: null} &&
            form.Value(FormFields.Server).Trim().Length == 0)
        {
            return "Enter the server URL.";
        }

        var server = ServerAddress.Normalize(form.Value(FormFields.Server));
        if (server.Length > 0 &&
            !Uri.TryCreate(server, UriKind.Absolute, out var uri) |
            (uri is not null && uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return "The server must be an http or https URL.";
        }

        foreach (var scope in descriptor.Scopes.Where(_ => _.Required))
        {
            if (form.Value(FormFields.Scope(scope.Id)).Trim().Length == 0)
            {
                return $"Enter the {scope.Label.ToLowerInvariant()}.";
            }
        }

        if (descriptor.UserLabel is not null &&
            form.Value(FormFields.User).Trim().Length == 0)
        {
            return $"Enter the {descriptor.UserLabel.ToLowerInvariant()}.";
        }

        var method = Method(form);
        var callbackPort = form.Value(FormFields.CallbackPort).Trim();
        if (method == AuthMethod.Browser &&
            callbackPort.Length > 0 &&
            (!int.TryParse(callbackPort, out var port) || port is < 1024 or > 65535))
        {
            return "The callback port must be between 1024 and 65535.";
        }

        if (method == AuthMethod.Token)
        {
            if (Token(form) is null &&
                !form.SignedIn)
            {
                return $"Enter the {descriptor.TokenLabel.ToLowerInvariant()}.";
            }
        }
        else if (!form.SignedIn)
        {
            return "Sign in first, or switch to a token.";
        }

        return null;
    }
}
