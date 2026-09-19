/// <summary>
/// One entry in a job's artifacts, which is a file or a folder: a file carries
/// <see cref="Url"/> to fetch it by, a folder carries <see cref="Files"/> holding its own entries.
/// The whole tree arrives in one response, so walking it costs no further requests.
/// </summary>
class GoCdArtifactEntry
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Url { get; set; }
    public long? Size { get; set; }
    public List<GoCdArtifactEntry> Files { get; set; } = [];
}
