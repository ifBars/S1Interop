namespace S1Interop.Core.Migration;

/// <summary>
/// Detects whether project source uses declarations that require the S1Interop generator package.
/// </summary>
public static class S1InteropGeneratorDetector
{
    private static readonly string[] GeneratorMarkers =
    [
        "S1InteropType",
        "S1InteropMember",
        "S1InteropGenerateUnityEventBridge",
        "S1InteropGenerateDelegateEventBridge"
    ];

    /// <summary>
    /// Searches project C# files for S1Interop generator attribute markers.
    /// </summary>
    /// <param name="projectPath">The path to the <c>.csproj</c> whose source tree should be searched.</param>
    /// <returns>True when a supported S1Interop declaration marker is found; otherwise, false.</returns>
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
