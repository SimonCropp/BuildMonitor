/// <summary>
/// The answer: whether it worked, and a body, which is JSON for the listing verbs and a message
/// otherwise.
/// </summary>
record Response(bool Ok, string Body)
{
    public static Response Success(string body = "") =>
        new(true, body);

    public static Response Error(string message) =>
        new(false, message);

    public string Build() =>
        $"ok: {(Ok ? "true" : "false")}\nbody: {Message.Encode(Body)}\n\n";

    public static bool TryParse(string text, [NotNullWhen(true)] out Response? response)
    {
        response = null;
        bool? ok = null;
        var body = "";
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
                case "ok":
                    ok = value == "true";
                    break;
                case "body":
                    body = Message.Decode(value);
                    break;
            }
        }

        if (ok is null)
        {
            return false;
        }

        response = new(ok.Value, body);
        return true;
    }
}
