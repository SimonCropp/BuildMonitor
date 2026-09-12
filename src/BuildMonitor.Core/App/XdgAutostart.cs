/// <summary>
/// A desktop entry in ~/.config/autostart, which every freedesktop session starts at login.
/// </summary>
sealed class XdgAutostart : IRunAtLogin
{
    public static string DesktopPath
    {
        get
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(config))
            {
                config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }

            return Path.Combine(config, "autostart", "buildmonitor.desktop");
        }
    }

    public bool Exists() =>
        File.Exists(DesktopPath);

    public void Set(bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopPath)!);
            File.WriteAllText(DesktopPath, Compose(ShimPath.Resolve()));
            return;
        }

        if (File.Exists(DesktopPath))
        {
            File.Delete(DesktopPath);
        }
    }

    public static string Compose(string shim) =>
        $"""
         [Desktop Entry]
         Type=Application
         Name=BuildMonitor
         Comment=Build and CI monitor
         Exec="{shim}"
         Terminal=false
         X-GNOME-Autostart-enabled=true

         """;
}
