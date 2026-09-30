/// <summary>
/// A file in one version of a package in the dotnet tool store, which lays a version out as
/// {tools}/.store/{package}/{version}/{package}/{version}/tools/{framework}/any/...
/// </summary>
record ToolStorePath(string PackageDirectory, string Version, string File)
{
    /// <summary>
    /// Null for a path outside the store, such as a build run from its bin directory. Windows
    /// paths ignore case, and the store names a directory in the case the package was pushed in.
    /// </summary>
    public static ToolStorePath? Parse(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var store = Array.FindIndex(segments, _ => string.Equals(_, ".store", StringComparison.OrdinalIgnoreCase));
        if (store <= 0 ||
            segments.Length < store + 4)
        {
            return null;
        }

        return new(
            string.Join(Path.DirectorySeparatorChar, segments[..(store + 2)]),
            segments[store + 2],
            segments[^1]);
    }
}
