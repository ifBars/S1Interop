using System.Text;
using System.Text.RegularExpressions;

namespace S1Interop.Core;

public static class PlayerCameraCompatRewriter
{
    private static readonly Regex CloseInterfaceCallRegex = new(
        @"(?<target>\b(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?:GamePlayerCamera|PlayerCamera)\.Instance)\.CloseInterface\((?<args>[^;\r\n]*)\);",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> FindFilesWithCloseInterfaceCalls(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return Array.Empty<string>();
        }

        return WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => !WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file))
            .Where(file => CloseInterfaceCallRegex.IsMatch(File.ReadAllText(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool RewriteFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        string original = File.ReadAllText(filePath);
        string rewritten = RewriteSource(original);

        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        File.WriteAllText(filePath, rewritten, Encoding.UTF8);
        return true;
    }

    public static string RewriteSource(string source) =>
        CloseInterfaceCallRegex.Replace(
            source,
            match =>
            {
                string target = match.Groups["target"].Value;
                string args = match.Groups["args"].Value.Trim();
                string invocationArgs = string.IsNullOrWhiteSpace(args)
                    ? target
                    : $"{target}, {args}";
                return $"S1Interop.Generated.S1PlayerCameraCompat.CloseInterface({invocationArgs});";
            });
}
