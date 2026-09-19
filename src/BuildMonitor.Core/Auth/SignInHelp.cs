/// <summary>
/// Turns a failed sign in into something the user can act on, for the one cause BuildMonitor can
/// name: an account of a kind the authority does not accept, which for Azure DevOps means a personal
/// Microsoft account against the work or school only <c>/organizations</c> authority.
/// <para>
/// How much can be said depends on how far the user got. Entra only reports the mismatch once it has
/// authenticated someone and redirected back, and says it as a wall of AADSTS text. Refused at the
/// address box it says nothing to BuildMonitor at all: the flow simply runs out of time with the
/// provider still waiting. So a certain answer for the first, and a conditional one for the second,
/// which is the more common way to hit it.
/// </para>
/// </summary>
static class SignInHelp
{
    // An account that authenticated and only then turned out to belong to no tenant the authority
    // accepts, which against /organizations is the personal Microsoft account case exactly: Entra
    // names the identity provider it came from. Only Entra emits this code, so finding it identifies
    // the provider as well as the cause.
    const string wrongKindOfAccount = "AADSTS50020";

    // An address Entra resolved to no account at all. A personal account can land here, but so does
    // a mistyped work address, and the two cannot be told apart from the code, so this one suggests
    // rather than concludes. Saying "that is a personal account" to someone who fat fingered their
    // own work address would be a confident wrong answer.
    const string noSuchAccount = "AADSTS50034";

    public static string Explain(ProviderDescriptor descriptor, AuthResult result)
    {
        var error = result.Error ?? "The sign in failed.";
        if (descriptor.SignInNote is null)
        {
            return error;
        }

        if (error.Contains(wrongKindOfAccount, StringComparison.Ordinal))
        {
            return $"That is a personal Microsoft account, which cannot sign in to {descriptor.Name}. {UseAToken(descriptor)}";
        }

        if (error.Contains(noSuchAccount, StringComparison.Ordinal))
        {
            return $"Microsoft did not recognise that address. Check it, and if it is a personal Microsoft account it cannot sign in this way: {LowerFirst(UseAToken(descriptor))}";
        }

        // Nothing came back, so this cannot say the address was the problem, only what to do if it
        // was. Worded as a condition for the user who was refused, and ignorable by the user who
        // simply walked away from the browser.
        if (result.TimedOut)
        {
            return $"{error} If the address was refused, it is a personal Microsoft account, which cannot sign in this way. {UseAToken(descriptor)}";
        }

        return error;
    }

    static string LowerFirst(string sentence) =>
        char.ToLowerInvariant(sentence[0]) + sentence[1..];

    static string UseAToken(ProviderDescriptor descriptor)
    {
        var label = descriptor.TokenLabel.ToLowerInvariant();
        return $"Set Sign in with to Token and paste {Article.For(label)} {label}.";
    }
}
