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
            settings = settings with
            {
                HistoryDays = new Settings().HistoryDays
            };
        }

        // Same reason, and a string's type default is null rather than the empty its initializer
        // says: a file written before CodeDirectory existed would put a null through the options
        // page and into the form, where everything downstream takes it for a string. The property
        // is not nullable, so an is null here reads as dead code; IsNullOrEmpty says the same thing
        // and is honest about the annotation being a compile time promise the reader does not keep.
        if (string.IsNullOrEmpty(settings.CodeDirectory))
        {
            settings = settings with
            {
                CodeDirectory = ""
            };
        }

        // Same reason once more, and worse for an array: the type default of an ImmutableArray is
        // not the empty one its initializer gives, and it throws when enumerated rather than
        // yielding nothing. A file written before this property existed would break the grouping of
        // every passing build.
        if (settings.GroupPrefixes.IsDefault)
        {
            settings = settings with
            {
                GroupPrefixes = []
            };
        }

        if (settings.Deferrals.IsDefault)
        {
            settings = settings with
            {
                Deferrals = []
            };
        }

        // And for a set, whose type default is null: a file written before OpenGroups existed
        // would throw at the first group drawn. The annotation says it cannot be null, and the
        // reader does not keep that promise, so this is not the dead check it reads as.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (settings.OpenGroups is null)
        {
            settings = settings with
            {
                OpenGroups = []
            };
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
