/// <summary>
/// The durations of the last few successful runs per pipeline, which is what the estimate for a
/// provider with no estimate of its own is derived from. Kept small on purpose: ten runs is
/// enough for a median and old runs describe a pipeline that has since changed.
/// </summary>
sealed class DurationHistory
{
    const int keep = 10;
    Lock gate = new();
    Dictionary<string, List<double>> seconds;

    public DurationHistory() :
        this(new())
    {
    }

    DurationHistory(Dictionary<string, List<double>> seconds) =>
        this.seconds = seconds;

    public void Record(string pipelineKey, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        lock (gate)
        {
            if (!seconds.TryGetValue(pipelineKey, out var list))
            {
                list = [];
                seconds[pipelineKey] = list;
            }

            list.Add(duration.TotalSeconds);
            if (list.Count > keep)
            {
                list.RemoveRange(0, list.Count - keep);
            }
        }
    }

    public TimeSpan? Median(string pipelineKey)
    {
        lock (gate)
        {
            return seconds.TryGetValue(pipelineKey, out var list) ? MedianOf(list) : null;
        }
    }

    public ImmutableDictionary<string, TimeSpan> Medians()
    {
        lock (gate)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, TimeSpan>();
            foreach (var (key, list) in seconds)
            {
                if (MedianOf(list) is { } median)
                {
                    builder[key] = median;
                }
            }

            return builder.ToImmutable();
        }
    }

    static TimeSpan? MedianOf(List<double> list)
    {
        if (list.Count == 0)
        {
            return null;
        }

        var sorted = list.Order().ToList();
        var middle = sorted.Count / 2;
        var median = sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
        return TimeSpan.FromSeconds(median);
    }

    public static DurationHistory Load(string path)
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            var json = File.ReadAllBytes(path);
            var read = JsonSerializer.Deserialize(json, HistoryContext.Default.DictionaryStringListDouble);
            return new(read ?? new());
        }
        catch (JsonException)
        {
            // A corrupt history only costs estimates until the pipelines have run again.
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] json;
        lock (gate)
        {
            json = JsonSerializer.SerializeToUtf8Bytes(seconds, HistoryContext.Default.DictionaryStringListDouble);
        }

        var temp = $"{path}.tmp";
        File.WriteAllBytes(temp, json);
        File.Move(temp, path, true);
    }
}
