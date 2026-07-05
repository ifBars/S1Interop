namespace S1Interop.Core.Analysis;

/// <summary>
/// Discovers C# project files from a workspace, project directory, or project file path.
/// </summary>
public static class ProjectDiscovery
{
    /// <summary>
    /// Returns project files that should be analyzed for the supplied path.
    /// </summary>
    /// <param name="path">A workspace directory, project directory, or <c>.csproj</c> file.</param>
    /// <returns>
    /// A single nearest project when one is found, all projects under a workspace otherwise, or an empty list when no project exists.
    /// </returns>
    public static IReadOnlyList<string> DiscoverProjects(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            return fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? new[] { fullPath }
                : Array.Empty<string>();
        }

        if (!Directory.Exists(fullPath))
        {
            return Array.Empty<string>();
        }

        string? nearest = FindNearestProject(fullPath);
        if (nearest is not null)
        {
            return new[] { nearest };
        }

        return EnumerateProjects(fullPath).OrderBy(project => project, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? FindNearestProject(string directory)
    {
        DirectoryInfo? current = new(directory);
        while (current is not null)
        {
            string[] projects = Directory.GetFiles(current.FullName, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length == 1)
            {
                return projects[0];
            }

            if (projects.Length > 1)
            {
                return null;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateProjects(string root)
    {
        foreach (string project in WorkspaceTraversal.EnumerateFiles(root, "*.csproj"))
        {
            yield return project;
        }
    }
}
