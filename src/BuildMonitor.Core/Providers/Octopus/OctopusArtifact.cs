/// <summary>
/// One file a deployment produced. Octopus reports no size for an artifact, so there is nothing
/// here to weigh a budget against and each is held to the per file cap while it copies.
/// </summary>
class OctopusArtifact
{
    public string Id { get; set; } = "";
    public string Filename { get; set; } = "";
}
