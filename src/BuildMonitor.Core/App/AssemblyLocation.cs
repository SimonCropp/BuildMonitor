static class AssemblyLocation
{
    public static string CurrentDirectory { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
}
