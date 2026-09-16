/// <summary>
/// A git checkout found under the code directory.
/// </summary>
/// <param name="Name">The folder's own name, which is what matches a provider that reports a bare
/// project name rather than a slug.</param>
/// <param name="Remote">The origin's path, "owner/repo" or "group/sub/project", or null where the
/// checkout has no origin.</param>
record LocalRepo(string Directory, string Name, string? Remote);
