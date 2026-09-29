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

    /// <summary>
    /// The response as the UTF-8 that goes on the wire, written into one array sized up front:
    /// a body is a listing or a log, and a string built first would be copied twice more.
    /// </summary>
    public byte[] Build()
    {
        var header = Ok ? "ok: true\n"u8 : "ok: false\n"u8;
        var bytes = new byte[header.Length + ProtocolLines.FieldLength("body"u8, Body) + 1];
        var span = bytes.AsSpan();
        var position = ProtocolLines.Write(span, header);
        position += ProtocolLines.WriteField(span[position..], "body"u8, Body);
        span[position] = (byte) '\n';
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> text, [NotNullWhen(true)] out Response? response)
    {
        response = null;
        bool? ok = null;
        var body = "";
        while (ProtocolLines.TryReadField(ref text, out var name, out var value))
        {
            if (name.SequenceEqual("ok"u8))
            {
                ok = value.SequenceEqual("true"u8);
            }
            else if (name.SequenceEqual("body"u8))
            {
                body = ProtocolLines.Decode(value);
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
