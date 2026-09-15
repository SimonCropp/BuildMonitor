/// <summary>
/// One file per secret, readable by the user alone. The fallback for a Linux session with no
/// Secret Service, and the shape the DPAPI store wraps.
/// </summary>
class FileSecretStore(string directory) : ISecretStore
{
    protected string Directory { get; } = directory;

    protected virtual string Extension => ".secret";

    public string? Read(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            return null;
        }

        return Decode(bytes);
    }

    public void Write(string key, string value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Directory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }

        var path = PathFor(key);
        var temp = $"{path}.tmp";
        File.WriteAllBytes(temp, Encode(value));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                temp,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }

        File.Move(temp, path, true);
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    protected virtual byte[] Encode(string value) =>
        Encoding.UTF8.GetBytes(value);

    protected virtual string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes);

    string PathFor(string key)
    {
        var safe = new StringBuilder(key.Length);
        foreach (var character in key)
        {
            safe.Append(char.IsAsciiLetterOrDigit(character) ||
                        character is '-' or '_' ? character : '_');
        }

        return Path.Combine(Directory, $"{safe}{Extension}");
    }
}
