/// <summary>
/// One instance of each string the providers parse, so a value the services repeat is held once
/// rather than once for every place it appears. A tray polling a large account held 58 thousand
/// live strings, more than half of them copies: "completed" three thousand times, "main" and the
/// owner's login two thousand times each, a Dependabot commit message 90 times, once for each run
/// of each workflow on that commit.
/// <para>
/// Interned for good, every commit message and run URL ever polled would stay for the life of the
/// tray. So entries live in two generations, as <see cref="ETagCache"/>'s do, and one not asked for
/// since the generation before is dropped. Dropping one costs nothing but a later copy: a string
/// still referenced lives on in what references it.
/// </para>
/// <para>
/// Looked up from the reader's text before any string is made, so a value already pooled costs no
/// allocation at all, where reading it and then pooling it made the copy the pool exists to avoid.
/// </para>
/// </summary>
sealed class StringPool(TimeSpan retention)
{
    public static StringPool Shared { get; } = new(TimeSpan.FromMinutes(30));

    /// <summary>
    /// Longer than this is read as a string of its own: hashing it would cost more than it saves,
    /// since a value this long is almost always a one-off commit message.
    /// </summary>
    const int maxBytes = 4096;

    const int stackChars = 256;

    ConcurrentDictionary<string, string> current = new(StringComparer.Ordinal);
    ConcurrentDictionary<string, string> previous = new(StringComparer.Ordinal);
    long rotated = Environment.TickCount64;

    /// <summary>
    /// The string or property name the reader is on, pooled. Any other token goes to
    /// <see cref="Utf8JsonReader.GetString"/>, which is what the converter this replaces did, so a
    /// number where a string was expected fails as it did before.
    /// </summary>
    public string? Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
        {
            return reader.GetString();
        }

        var bytes = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (bytes > maxBytes)
        {
            return reader.GetString();
        }

        // Unescaped, the text is never longer in chars than it was in UTF-8 bytes.
        if (bytes <= stackChars)
        {
            Span<char> buffer = stackalloc char[stackChars];
            var length = reader.CopyString(buffer);
            return Get(buffer[..length]);
        }

        var rented = ArrayPool<char>.Shared.Rent((int) bytes);
        try
        {
            var length = reader.CopyString(rented);
            return Get(rented.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    public string Get(CharSpan value)
    {
        RotateIfDue();
        var generation = current;
        if (generation.GetAlternateLookup<CharSpan>().TryGetValue(value, out var pooled))
        {
            return pooled;
        }

        if (previous.GetAlternateLookup<CharSpan>().TryGetValue(value, out pooled))
        {
            return generation.GetOrAdd(pooled, pooled);
        }

        var text = value.ToString();
        return generation.GetOrAdd(text, text);
    }

    /// <summary>
    /// Drops whatever was not asked for since the call before. On the clock for
    /// <see cref="Shared"/>, which every connection's polls read through, so no one poller's
    /// cycle decides how long another's values are kept.
    /// </summary>
    public void Rotate()
    {
        previous = current;
        current = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Two threads finding a rotation due rotate once, as only the one that moves
    /// <see cref="rotated"/> does. One reading a generation mid-rotation at worst misses a value
    /// and makes a copy of it.
    /// </summary>
    void RotateIfDue()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref rotated);
        if (now - last < (long) retention.TotalMilliseconds ||
            Interlocked.CompareExchange(ref rotated, now, last) != last)
        {
            return;
        }

        Rotate();
    }
}
