class CircleCiAction
{
    // The pair an action's output is addressed by.
    public int Step { get; set; }
    public int Index { get; set; }
    public bool? Failed { get; set; }
    public string? Status { get; set; }
    public bool HasOutput { get; set; }
}
