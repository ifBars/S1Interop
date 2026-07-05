namespace S1Interop.Core.Analysis;

/// <summary>
/// Analyzes S1Interop readiness for projects under a workspace or project path.
/// </summary>
public sealed class WorkspaceAnalyzer
{
    private readonly CsprojAnalyzer projectAnalyzer = new();

    /// <summary>
    /// Discovers C# projects under the supplied path and analyzes their runtime configuration evidence.
    /// </summary>
    /// <param name="path">A workspace directory, project directory, or <c>.csproj</c> file to analyze.</param>
    /// <returns>The workspace analysis result, including discovered projects and diagnostics.</returns>
    public WorkspaceAnalysis Analyze(string path)
    {
        string fullPath = Path.GetFullPath(path);
        IReadOnlyList<string> projects = ProjectDiscovery.DiscoverProjects(fullPath);
        ProjectAnalysis[] analyses = projects.Select(projectAnalyzer.AnalyzeProject).ToArray();
        return new WorkspaceAnalysis(fullPath, analyses);
    }
}
