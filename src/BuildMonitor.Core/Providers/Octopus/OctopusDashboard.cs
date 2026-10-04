class OctopusDashboard
{
    public required List<OctopusDashboardItem> Items { get; init; }
    public required List<OctopusEnvironment> Environments { get; init; }
    // Set when a large space returned fewer projects than asked for.
    public int? ProjectLimit { get; init; }
}