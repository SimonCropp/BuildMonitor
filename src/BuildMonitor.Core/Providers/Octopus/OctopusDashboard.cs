class OctopusDashboard
{
    public required List<OctopusDashboardItem> Items { get; set; }
    public required List<OctopusEnvironment> Environments { get; set; }
    // Set when a large space returned fewer projects than asked for.
    public int? ProjectLimit { get; set; }
}