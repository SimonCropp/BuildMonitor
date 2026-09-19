/// <summary>
/// An artifact that ran past the bytes allowed for it. Thrown rather than reported as a short
/// download, because a truncated archive is a corrupt archive: an assistant handed one gets an
/// unpacking error naming nothing, where a file that is absent and said to be absent is an answer.
/// <para>
/// Whatever was written before this is the caller's to delete. The download only ever writes to
/// the stream it was handed, so it cannot clean up after itself.
/// </para>
/// </summary>
/// <param name="declared">
/// The size the service declared, or null where it declared none and the limit was reached while
/// copying. Jenkins reports no artifact size anywhere in its API, so null is the ordinary case
/// rather than the odd one.
/// </param>
sealed class ArtifactTooLargeException(long? declared, long limit) :
    Exception(declared is { } size
        ? $"{ByteSize.Human(size)}, over the {ByteSize.Human(limit)} allowed for it"
        : $"Over the {ByteSize.Human(limit)} allowed for it")
{
    public long? Declared { get; } = declared;
    public long Limit { get; } = limit;
}
