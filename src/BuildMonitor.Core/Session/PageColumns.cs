/// <summary>
/// What the builds page reads of every row rather than of the rows in view, and the rows it was read
/// from: what each author is called, each repository's pipelines, and the texts the columns are
/// sized from. None of it reads the clock, so <see cref="ScreenBuilder"/> hands it back for as long
/// as <see cref="Projected"/> stands.
/// </summary>
/// <param name="Authors">What the author column calls each person who broke a build, by the name
/// the build gives them.</param>
/// <param name="Siblings">The pipeline names of each repository among the builds shown.</param>
/// <param name="AuthorNames">The distinct values of <paramref name="Authors"/>, which the author
/// column is sized from.</param>
sealed record PageColumns(
    ProjectedRows Projected,
    IReadOnlyDictionary<string, string> Authors,
    IReadOnlyDictionary<(string ConnectionId, string RepoName), List<string>> Siblings,
    List<string> Names,
    List<string> GroupNames,
    List<string> Details,
    List<string> AuthorNames,
    List<string> MemberNames,
    List<string> MarkedGroupNames,
    List<string> MarkedDetails);
