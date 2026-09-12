/// <summary>
/// A per user launchd agent in ~/Library/LaunchAgents, loaded into the GUI session. Not
/// SMAppService, which only registers helpers inside an app bundle.
/// </summary>
sealed class LaunchAgent : IRunAtLogin
{
    public const string Label = "com.simoncropp.buildmonitor";

    public static string PlistPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{Label}.plist");

    public bool Exists() =>
        File.Exists(PlistPath);

    public void Set(bool enabled)
    {
        var domain = $"gui/{ProcessRunner.Run("/usr/bin/id", ["-u"]).Output.Trim()}";
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, Compose(ShimPath.Resolve()));
            ProcessRunner.Run("/bin/launchctl", ["bootstrap", domain, PlistPath]);
            return;
        }

        if (File.Exists(PlistPath))
        {
            ProcessRunner.Run("/bin/launchctl", ["bootout", $"{domain}/{Label}"]);
            File.Delete(PlistPath);
        }
    }

    /// <summary>
    /// RunAtLoad without KeepAlive: start once at login, and let the user quit it.
    /// </summary>
    public static string Compose(string shim) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
         <plist version="1.0">
         <dict>
             <key>Label</key>
             <string>{Label}</string>
             <key>ProgramArguments</key>
             <array>
                 <string>{System.Security.SecurityElement.Escape(shim)}</string>
             </array>
             <key>RunAtLoad</key>
             <true/>
             <key>LimitLoadToSessionType</key>
             <string>Aqua</string>
         </dict>
         </plist>

         """;
}
