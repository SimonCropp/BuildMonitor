class CircleCiProject
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? OrganizationName { get; set; }
    public CircleCiVcsInfo? VcsInfo { get; set; }
}
