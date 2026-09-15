class TravisJob
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string State { get; set; } = "";
    public bool AllowFailure { get; set; }
}