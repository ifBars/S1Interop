namespace S1Interop.Core;

public sealed class WorkspaceAnalyzer
{
    private readonly CsprojAnalyzer projectAnalyzer = new();

    public WorkspaceAnalysis Analyze(string path)
    {
        string fullPath = Path.GetFullPath(path);
        IReadOnlyList<string> projects = ProjectDiscovery.DiscoverProjects(fullPath);
        ProjectAnalysis[] analyses = projects.Select(projectAnalyzer.AnalyzeProject).ToArray();
        return new WorkspaceAnalysis(fullPath, analyses);
    }
}
