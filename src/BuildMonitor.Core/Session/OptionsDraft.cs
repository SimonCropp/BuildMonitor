/// <summary>
/// Reads the options page back into <see cref="Settings"/>.
/// </summary>
static class OptionsDraft
{
    public static bool TryBuild(FormState form, Settings current, [NotNullWhen(true)] out Settings? settings, [NotNullWhen(false)] out string? error)
    {
        settings = null;
        if (!TryInterval(form.Value(FormFields.PollInterval), out var poll))
        {
            error = "The poll interval must be between 5 and 3600 seconds.";
            return false;
        }

        if (!TryInterval(form.Value(FormFields.RunningPollInterval), out var running))
        {
            error = "The poll interval while running must be between 5 and 3600 seconds.";
            return false;
        }

        if (!int.TryParse(form.Value(FormFields.Port), out var port) ||
            port is < 1024 or > 65535)
        {
            error = "The port must be between 1024 and 65535.";
            return false;
        }

        error = null;
        settings = current with
        {
            RunAtStartup = form.Flag(FormFields.RunAtStartup),
            ShowWindowAtStart = form.Flag(FormFields.ShowWindowAtStart),
            ShowOtherBranches = form.Flag(FormFields.ShowOtherBranches),
            ShowForksAndCollaborations = form.Flag(FormFields.ShowForks),
            NotifyOnFailure = form.Flag(FormFields.NotifyOnFailure),
            Theme = Enum.TryParse<Theme>(form.Value(FormFields.Theme), out var theme) ? theme : current.Theme,
            PollIntervalSeconds = poll,
            RunningPollIntervalSeconds = running,
            Port = port
        };
        return true;
    }

    static bool TryInterval(string text, out int seconds) =>
        int.TryParse(text, out seconds) && seconds is >= 5 and <= 3600;
}
