namespace S1Interop.Core.Migration;

public static class S1InteropGeneratorDetector
{
    private static readonly string[] GeneratorMarkers =
    [
        "S1InteropType",
        "S1InteropMember",
        "S1InteropGenerateUnityEventBridge",
        "S1InteropGenerateDelegateEventBridge"
    ];

    public static bool ProjectUsesGeneratorAttributes(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return false;
        }

        return WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => !WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file))
            .Any(FileContainsGeneratorMarker);
    }

    private static bool FileContainsGeneratorMarker(string filePath)
    {
        string source = File.ReadAllText(filePath);
        return GeneratorMarkers.Any(marker => source.Contains(marker, StringComparison.Ordinal));
    }
}
