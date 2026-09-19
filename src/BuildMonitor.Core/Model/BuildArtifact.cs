/// <summary>
/// One file a build produced, as the service lists it and before anything is downloaded. Listing
/// and downloading are separate so the budget can be spent on the files most likely to say why a
/// build failed, which needs their names and sizes first.
/// </summary>
/// <param name="Id">
/// Whatever <see cref="IProvider.DownloadArtifact"/> needs to fetch this one: a numeric id, a path
/// inside the build, a whole URL. Opaque to everything but the provider that wrote it, as
/// <see cref="Build.ProviderRef"/> is.
/// </param>
/// <param name="Name">
/// What the service calls the file. Several providers report a path rather than a name, so this
/// reaches the filesystem only through a sanitiser: a <c>..</c> in one would otherwise write
/// outside the directory it was meant for.
/// </param>
/// <param name="Bytes">
/// The size the listing gave, or null where the service does not say, as Jenkins never does. Null
/// is not zero: it is a size no budget can plan around, so it is fetched under a cap instead and
/// found out while copying.
/// </param>
/// <param name="Unavailable">
/// Why the service will not serve this one although it still lists it, in the service's own word:
/// a GitHub artifact past its retention is "expired". Null for one that can be fetched. Carried
/// rather than dropped so a prompt can say the evidence existed and is gone, which reads very
/// differently from a build that published nothing.
/// </param>
record BuildArtifact(string Id, string Name, long? Bytes, string? Unavailable = null);
