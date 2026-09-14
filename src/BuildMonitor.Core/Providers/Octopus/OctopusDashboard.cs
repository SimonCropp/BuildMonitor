class OctopusDashboard
{
    public List<OctopusDashboardItem> Items { get; set; } = [];
    public List<OctopusEnvironment> Environments { get; set; } = [];
    // Set when a large space returned fewer projects than asked for.
    public int? ProjectLimit { get; set; }
}