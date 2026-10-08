class CircleCiStep
{
    public string Name { get; set; } = "";
    // One per parallel run of the step.
    public List<CircleCiAction> Actions { get; set; } = [];
}
