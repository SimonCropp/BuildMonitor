/// <summary>
/// One request over the loopback socket: a few labelled lines, every value base64, ended by an
/// empty line. Text rather than JSON so the launcher needs no serializer, base64 so a key with
/// a newline in it cannot end the message early. Unknown lines are ignored, so a newer launcher
/// can talk to an older tray.
/// </summary>
record Message(Verb Verb, string? Key = null, string? Body = null)
{
    const int version = 1;

    public string Build()
    {
        var builder = new StringBuilder();
        builder.Append("version: ").Append(version).Append('\n');
        builder.Append("verb: ").Append(Verb.ToString().ToLowerInvariant()).Append('\n');
        if (Key is not null)
        {
            builder.Append("key: ").Append(Encode(Key)).Append('\n');
        }

        if (Body is not null)
        {
            builder.Append("body: ").Append(Encode(Body)).Append('\n');
        }

        builder.Append('\n');
        return builder.ToString();
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out Message? message)
    {
        message = null;
        Verb? verb = null;
        string? key = null;
        string? body = null;
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (name)
            {
                case "verb":
                    if (!Enum.TryParse<Verb>(value, true, out var parsed))
                    {
                        return false;
                    }

                    verb = parsed;
                    break;
                case "key":
                    key = Decode(value);
                    break;
                case "body":
                    body = Decode(value);
                    break;
            }
        }

        if (verb is null)
        {
            return false;
        }

        message = new(verb.Value, key, body);
        return true;
    }

    public static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static string Decode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return "";
        }
    }
}
