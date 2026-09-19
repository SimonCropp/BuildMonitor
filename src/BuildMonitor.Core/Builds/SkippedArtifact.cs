/// <summary>
/// An artifact that was listed but not downloaded, with the reason written once here so every
/// prompt says it the same way.
/// <para>
/// Carried rather than dropped. A list that quietly leaves out the one file an assistant is looking
/// for reads as a build that never published it, and an assistant that concludes that stops
/// looking. <see cref="Bytes"/> is null where the service never said a size.
/// </para>
/// </summary>
record SkippedArtifact(string Name, long? Bytes, string Reason);
