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
    /// The first thing wrong with the draft, at the field it is about, or null when it can be saved.
    /// A missing value is named by the field's label as written, at the start of the sentence: put
    /// lowercased into "Enter the ...", "API token" read as "api token", and no rule for which first
    /// letters can be lowered keeps "Atlassian account email" and drops "Organization".
    /// </summary>
    public static FormError? Validate(ConnectionFormState form)
    {
        var descriptor = form.Descriptor;
        if (descriptor is {SelfHosted: true, DefaultServer: null} &&
            form.Value(FormFields.Server).Trim().Length == 0)
        {
            return new("Enter the server URL.", FormFields.Server);
        }

        var server = ServerAddress.Normalize(form.Value(FormFields.Server));
        if (server.Length > 0 &&
            !Uri.TryCreate(server, UriKind.Absolute, out var uri) |
            (uri is not null && uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return new("The server must be an http or https URL.", FormFields.Server);
        }

        foreach (var scope in descriptor.Scopes.Where(_ => _.Required))
        {
            var id = FormFields.Scope(scope.Id);
            if (form.Value(id).Trim().Length == 0)
            {
                return Required(scope.Label, id);
            }
        }

        if (descriptor.UserLabel is not null &&
            form.Value(FormFields.User).Trim().Length == 0)
        {
            return Required(descriptor.UserLabel, FormFields.User);
        }

        var method = Method(form);
        var callbackPort = form.Value(FormFields.CallbackPort).Trim();
        if (method == AuthMethod.Browser &&
            callbackPort.Length > 0 &&
            (!int.TryParse(callbackPort, out var port) || port is < 1024 or > 65535))
        {
            return new("The callback port must be between 1024 and 65535.", FormFields.CallbackPort);
        }

        if (method == AuthMethod.Token)
        {
            if (Token(form) is null &&
                !form.SignedIn)
            {
                return Required(descriptor.TokenLabel, FormFields.Token);
            }
        }
        else if (!form.SignedIn)
        {
            return new("Sign in first, or switch to a token.", FormFields.Auth);
        }

        return null;
    }

    static FormError Required(string label, string field) =>
        new($"{label} is required.", field);
}
