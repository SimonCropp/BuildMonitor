class GoCdStage
{
    public string Name { get; set; } = "";
    public string? Counter { get; set; }
    public string? Status { get; set; }
    public string? Result { get; set; }
    public bool Scheduled { get; set; }
    public List<GoCdJob> Jobs { get; set; } = [];
}