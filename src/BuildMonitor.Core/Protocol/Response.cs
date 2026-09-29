/// <summary>
/// The answer: whether it worked, and a body, which is JSON for the listing verbs and a message
/// otherwise.
/// <para>
/// The body is held as whichever it was made as. A message or a log is text, written straight into
/// the wire array. JSON is the UTF-8 the serializer writes, and an answer read off the wire is the
/// UTF-8 that arrived, which <see cref="Read{T}"/> parses as it is. Held as a string, a listing went
/// from UTF-8 to UTF-16 and back on each side of the socket, a copy of a few megabytes each time.
/// </para>
/// </summary>
sealed record Response
{
    string? text;
    ReadOnlyMemory<byte>? utf8;

    Response(bool ok, string? text, ReadOnlyMemory<byte>? utf8)
    {
        Ok = ok;
        this.text = text;
        this.utf8 = utf8;
    }

    public bool Ok { get; }

    /// <summary>
    /// The body as text, decoded once where it is held as UTF-8.
    /// </summary>
    public string Body => text ??= Encoding.UTF8.GetString(utf8!.Value.Span);

    public static Response Success(string body = "") =>
        new(true, body, null);

    public static Response Success<T>(T value, JsonTypeInfo<T> info) =>
        new(true, null, JsonSerializer.SerializeToUtf8Bytes(value, info));

    public static Response Error(string message) =>
        new(false, message, null);

    /// <summary>
    /// The body parsed as JSON, from the UTF-8 where that is what is held.
    /// </summary>
    public T? Read<T>(JsonTypeInfo<T> info)
    {
        if (utf8 is { } bytes)
        {
            return JsonSerializer.Deserialize(bytes.Span, info);
        }

        return JsonSerializer.Deserialize(text!, info);
    }

    /// <summary>
    /// The response as the UTF-8 that goes on the wire, written into one array sized up front:
    /// a body is a listing or a log, and a string built first would be copied twice more.
    /// </summary>
    public byte[] Build()
    {
        var header = Ok ? "ok: true\n"u8 : "ok: false\n"u8;
        var bodyLength = utf8 is { } body ? ProtocolLines.FieldLength("body"u8, body.Length) : ProtocolLines.FieldLength("body"u8, text!);
        var bytes = new byte[header.Length + bodyLength + 1];
        var span = bytes.AsSpan();
        var position = ProtocolLines.Write(span, header);
        position += utf8 is { } json ? ProtocolLines.WriteField(span[position..], "body"u8, json.Span) : ProtocolLines.WriteField(span[position..], "body"u8, text!);
        span[position] = (byte) '\n';
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> text, [NotNullWhen(true)] out Response? response)
    {
        response = null;
        bool? ok = null;
        ReadOnlyMemory<byte> body = default;
        while (ProtocolLines.TryReadField(ref text, out var name, out var value))
        {
            if (name.SequenceEqual("ok"u8))
            {
                ok = value.SequenceEqual("true"u8);
            }
            else if (name.SequenceEqual("body"u8))
            {
                body = ProtocolLines.DecodeBytes(value);
            }
        }

        if (ok is null)
        {
            return false;
        }

        response = new(ok.Value, null, body);
        return true;
    }

    /// <summary>
    /// Equal when they say the same, whichever way each holds its body: one read off the wire holds
    /// UTF-8 where the one that was sent held text.
    /// </summary>
    public bool Equals(Response? other) =>
        other is not null &&
        Ok == other.Ok &&
        Body == other.Body;

    public override int GetHashCode() =>
        HashCode.Combine(Ok, Body);
}
