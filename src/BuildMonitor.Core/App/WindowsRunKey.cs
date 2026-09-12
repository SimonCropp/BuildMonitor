/// <summary>
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run. The value is a command line, so the path
/// is quoted: an unquoted path with a space in it is read as a program and an argument.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class WindowsRunKey : IRunAtLogin
{
    const string valueName = "BuildMonitor";
    const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public bool Exists()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) is not null;
    }

    public void Set(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true) ??
                        throw new InvalidOperationException("The Run key does not exist");
        if (enabled)
        {
            key.SetValue(valueName, Value(ShimPath.Resolve()));
        }
        else
        {
            key.DeleteValue(valueName, false);
        }
    }

    public static string Value(string path) =>
        $"\"{path}\"";
}
