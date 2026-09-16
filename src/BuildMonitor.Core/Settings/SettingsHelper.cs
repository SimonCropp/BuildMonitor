static class SettingsHelper
{
    /// <summary>
    /// Sync rather than async, deliberately: this runs once at startup before the icon appears,
    /// and the file is a few hundred bytes.
    /// </summary>
    public static Settings Read()
    {
        var path = AppPaths.Settings;
        if (!File.Exists(path))
        {
            return new();
        }

        var json = File.ReadAllBytes(path);
        if (json.Length == 0)
        {
            return new();
        }

        var settings = JsonSerializer.Deserialize(json, SettingsContext.Default.Settings) ?? new();
        // The generated reader sets an init property the file does not name to its type's default,
        // not the initializer's, so a file written before HistoryDays existed read it as 0 and hid
        // every build older than today.
        if (settings.HistoryDays < 1)
        {
            settings = settings with { HistoryDays = new Settings().HistoryDays };
        }

        // Same reason, and a string's type default is null rather than the empty its initializer
        // says: a file written before CodeDirectory existed would put a null through the options
        // page and into the form, where everything downstream takes it for a string.
        if (settings.CodeDirectory is null)
        {
            settings = settings with { CodeDirectory = "" };
        }

        return settings;
    }

    /// <summary>
    /// Written to a temp file beside the target and then moved over it. Deleting and rewriting
    /// left nothing, or a truncated file, when the process was killed between the two.
    /// </summary>
    public static async Task Write(Settings settings)
    {
        var path = AppPaths.Settings;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SettingsContext.Default.Settings);
        }

        await Swap(temp, path);
    }

    /// <summary>
    /// A file that was just written can still be held by an indexer or a virus scanner for a
    /// moment, so the move is retried rather than trusted.
    /// </summary>
    static async Task Swap(string temp, string path)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                File.Move(temp, path, true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(20);
            }
        }
    }
}
