/// <summary>
/// Reads the connection editor's fields back into a <see cref="Connection"/>. Kept apart from
/// the transitions because it is the one place a form's strings are interpreted, and its errors
/// are the messages the form shows.
/// </summary>
static class ConnectionDraft
{
    public static ProviderDescriptor Descriptor(FormState form) =>
        ProviderDescriptors.ByName(form.Value(FormFields.Provider)) ?? ProviderDescriptors.All[0];

    public static AuthMethod Method(FormState form) =>
        Enum.TryParse<AuthMethod>(form.Value(FormFields.Auth), out var method) ? method : AuthMethod.Token;

    /// <summary>
    /// The connection as currently described, without checking it is complete. Enough to start a
    /// sign in or a test.
    /// </summary>
    public static Connection Build(FormState form)
    {
        var descriptor = Descriptor(form);
        var scope = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var field in descriptor.Scopes)
        {
            var value = form.Value(FormFields.Scope(field.Id)).Trim();
            if (value.Length > 0)
            {
                scope[field.Id] = value;
            }
        }

        var server = form.Value(FormFields.Server).Trim().TrimEnd('/');
        var user = form.Value(FormFields.User).Trim();
        var clientId = form.Value(FormFields.ClientId).Trim();
        return new()
        {
            Id = form.EditingConnectionId ?? form.DraftConnectionId ?? Guid.NewGuid().ToString("N"),
            ProviderId = descriptor.Id,
            Name = form.Value(FormFields.Name).Trim() is { Length: > 0 } name ? name : descriptor.Name,
            Server = server.Length == 0 ? null : server,
            Scope = scope.ToImmutable(),
            Auth = Method(form),
            User = user.Length == 0 ? null : user,
            ClientId = clientId.Length == 0 ? null : clientId
        };
    }

    public static string? Token(FormState form)
    {
        var token = form.Value(FormFields.Token).Trim();
        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// The first thing wrong with the draft, or null when it can be saved.
    /// </summary>
    public static string? Validate(FormState form)
    {
        var descriptor = Descriptor(form);
        if (descriptor.SelfHosted &&
            descriptor.DefaultServer is null &&
            form.Value(FormFields.Server).Trim().Length == 0)
        {
            return "Enter the server URL.";
        }

        var server = form.Value(FormFields.Server).Trim();
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
