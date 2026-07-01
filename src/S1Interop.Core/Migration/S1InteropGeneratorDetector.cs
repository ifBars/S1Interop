namespace S1Interop.Core;

public static class S1InteropGeneratorDetector
{
    public static bool ProjectUsesGeneratorAttributes(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return false;
        }

        return WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => !WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file))
            .Any(file => File.ReadAllText(file).Contains("S1InteropType", StringComparison.Ordinal));
    }
}
