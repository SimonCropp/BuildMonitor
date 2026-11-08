class JenkinsRevision
{
    // The commit the git plugin built, which it exports as SHA1.
    [JsonPropertyName("SHA1")]
    public string? Sha1 { get; set; }

    public List<JenkinsBranch>? Branch { get; set; }
}