static class BuildExtensions
{
    public static string RunNumberLabel(this Build build) =>
        build.RunNumber.Length == 0 ? "" : $"#{build.RunNumber}";
}
