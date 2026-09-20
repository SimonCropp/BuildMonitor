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
            if (seconds.TryGetValue(pipelineKey, out var list))
            {
                return MedianOf(list);
            }

            return null;
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

    /// <summary>
    /// The fastest and slowest recent run of each pipeline, which the poller aims its running
    /// interval between.
    /// </summary>
    public ImmutableDictionary<string, DurationRange> Ranges()
    {
        lock (gate)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, DurationRange>();
            foreach (var (key, list) in seconds)
            {
                if (list.Count > 0)
                {
                    var min = TimeSpan.FromSeconds(list.Min());
                    var max = TimeSpan.FromSeconds(list.Max());
                    builder[key] = new(min, max);
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

        // Sorted on the stack: a poll takes every pipeline's median while the window is open, and a
        // sorted copy of each list was several allocations a pipeline. A loaded history is only
        // trimmed to keep once its pipeline runs again, so a list can be longer.
        var sorted = list.Count <= 64 ? stackalloc double[list.Count] : new double[list.Count];
        list.CopyTo(sorted);
        sorted.Sort();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 0
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
