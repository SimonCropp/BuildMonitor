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

        return JsonSerializer.Deserialize(json, SettingsContext.Default.Settings) ?? new();
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
