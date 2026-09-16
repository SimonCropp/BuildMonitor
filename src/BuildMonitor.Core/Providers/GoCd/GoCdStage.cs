class GoCdStage
{
    public string Name { get; set; } = "";
    public string? Counter { get; set; }
    public string? Status { get; set; }
    public string? Result { get; set; }
    public bool Scheduled { get; set; }
    // Whether the user may operate this stage, which re-running its failed jobs or cancelling it needs.
    public bool? OperatePermission { get; set; }
    public List<GoCdJob> Jobs { get; set; } = [];
}