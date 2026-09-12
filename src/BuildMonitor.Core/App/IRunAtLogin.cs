/// <summary>
/// The desktop's own start-at-login mechanism. <see cref="Exists"/> reads the artefact rather
/// than a setting, so the options checkbox reports what will actually happen at the next login.
/// </summary>
interface IRunAtLogin
{
    bool Exists();

    void Set(bool enabled);
}

static class RunAtLogin
{
    public static IRunAtLogin ForPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRunKey();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new LaunchAgent();
        }

        return new XdgAutostart();
    }
}
