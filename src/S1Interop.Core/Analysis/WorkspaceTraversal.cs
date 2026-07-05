namespace S1Interop.Core.Analysis;

internal static class WorkspaceTraversal
{
    public static readonly HashSet<string> CommonExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".agents",
        ".codex",
        ".cursor",
        ".git",
        ".idea",
        ".opencode",
        ".vs",
        ".vscode",
        "artifacts",
        "AssetRipper",
        "AssetRipperExport",
        "bin",
        "Cpp2IL",
        "cpp2il_out",
        "ErrorAnalyzer",
        "Il2CppAssemblies",
        "Il2CppInterop",
        "MelonLoader",
        "ModcreatorSchedule1",
        "node_modules",
        "obj",
        "Pathfinding",
        "s1interop-runs",
        "S1Interop",
        "S1Interop.Generated",
        "S1MCPServer",
        "Steamworks.NET",
        "target",
        "tests",
        "tmp",
        "tools",
        "UnityExplorer",
        "UniverseLib"
    };

    public static IEnumerable<string> EnumerateFiles(
        string root,
        string searchPattern,
        IReadOnlySet<string>? excludedDirectoryNames = null)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        excludedDirectoryNames ??= CommonExcludedDirectoryNames;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string file in EnumerateTopLevelFiles(directory, searchPattern))
            {
                yield return file;
            }

            foreach (string child in EnumerateDirectories(directory, excludedDirectoryNames))
            {
                pending.Push(child);
            }
        }
    }

    public static IEnumerable<string> EnumerateDirectories(
        string directory,
        IReadOnlySet<string>? excludedDirectoryNames = null)
    {
        excludedDirectoryNames ??= CommonExcludedDirectoryNames;
        string[] children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception ex) when (IsTraversalException(ex))
        {
            yield break;
        }

        foreach (string child in children)
        {
            if (ShouldSkipDirectory(child, excludedDirectoryNames))
            {
                continue;
            }

            yield return child;
        }
    }

    public static bool HasExcludedPathPart(
        string root,
        string path,
        IReadOnlySet<string>? excludedDirectoryNames = null)
    {
        excludedDirectoryNames ??= CommonExcludedDirectoryNames;
        string relativePath = Path.GetRelativePath(root, path);
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(excludedDirectoryNames.Contains);
    }

    private static IEnumerable<string> EnumerateTopLevelFiles(string directory, string searchPattern)
    {
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (IsTraversalException(ex))
        {
            yield break;
        }

        foreach (string file in files)
        {
            yield return file;
        }
    }

    private static bool ShouldSkipDirectory(string directory, IReadOnlySet<string> excludedDirectoryNames)
    {
        string directoryName = Path.GetFileName(directory);
        if (excludedDirectoryNames.Contains(directoryName) ||
            directoryName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (IsTraversalException(ex))
        {
            return true;
        }
    }

    private static bool IsTraversalException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DirectoryNotFoundException;
}
