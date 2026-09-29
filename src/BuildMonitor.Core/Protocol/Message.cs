/// <summary>
/// One request over the loopback socket: a few labelled lines, every value base64, ended by an
/// empty line. Text rather than JSON so the launcher needs no serializer, base64 so a key with
/// a newline in it cannot end the message early. Unknown lines are ignored, so a newer launcher
/// can talk to an older tray.
/// </summary>
record Message(Verb Verb, string? Key = null, string? Body = null)
{
    const int version = 1;

    /// <summary>
    /// The message as the UTF-8 that goes on the wire.
    /// </summary>
    public byte[] Build()
    {
        var header = $"version: {version}\nverb: {Verb.ToString().ToLowerInvariant()}\n";
        var length = Encoding.UTF8.GetByteCount(header) + 1;
        if (Key is not null)
        {
            length += ProtocolLines.FieldLength("key"u8, Key);
        }

        if (Body is not null)
        {
            length += ProtocolLines.FieldLength("body"u8, Body);
        }

        var bytes = new byte[length];
        var span = bytes.AsSpan();
        var position = Encoding.UTF8.GetBytes(header, span);
        if (Key is not null)
        {
            position += ProtocolLines.WriteField(span[position..], "key"u8, Key);
        }

        if (Body is not null)
        {
            position += ProtocolLines.WriteField(span[position..], "body"u8, Body);
        }

        span[position] = (byte) '\n';
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> text, [NotNullWhen(true)] out Message? message)
    {
        message = null;
        Verb? verb = null;
        string? key = null;
        string? body = null;
        while (ProtocolLines.TryReadField(ref text, out var name, out var value))
        {
            if (name.SequenceEqual("verb"u8))
            {
                if (!Enum.TryParse<Verb>(Encoding.UTF8.GetString(value), true, out var parsed))
                {
                    return false;
                }

                verb = parsed;
            }
            else if (name.SequenceEqual("key"u8))
            {
                key = ProtocolLines.Decode(value);
            }
            else if (name.SequenceEqual("body"u8))
            {
                body = ProtocolLines.Decode(value);
            }
        }

        if (verb is null)
        {
            return false;
        }

        message = new(verb.Value, key, body);
        return true;
    }
}
