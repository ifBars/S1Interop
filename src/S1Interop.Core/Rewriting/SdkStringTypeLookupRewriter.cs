using System.Text.RegularExpressions;

namespace S1Interop.Core;

public static class SdkStringTypeLookupRewriter
{
    private static readonly Regex TypeLookupRegex = new(
        @"(?<call>AccessTools\.TypeByName|(?:System\.)?Type\.GetType)\s*\(\s*(?<literal>@?""(?<type>(?:Il2Cpp)?ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*){2,})"")\s*(?:,\s*(?:throwOnError\s*:\s*)?false)?\s*\)",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> FindFilesWithRewritableTypeLookups(
        string projectPath,
        IReadOnlyList<SdkTypeAlias> aliases)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory) || aliases.Count == 0)
        {
            return Array.Empty<string>();
        }

        return WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => !WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file))
            .Where(file => !IsGeneratedFacade(file))
            .Where(file => CanRewriteSource(File.ReadAllText(file), aliases))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string RewriteSource(string source, IReadOnlyList<SdkTypeAlias> aliases)
    {
        if (aliases.Count == 0)
        {
            return source;
        }

        Dictionary<string, string> aliasesByRuntimeTypeName = aliases
            .SelectMany(alias => new[]
            {
                new KeyValuePair<string, string>(alias.MonoType, alias.Alias),
                new KeyValuePair<string, string>(alias.Il2CppType, alias.Alias)
            })
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.Value).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        return TypeLookupRegex.Replace(
            source,
            match =>
            {
                string typeName = match.Groups["type"].Value;
                return aliasesByRuntimeTypeName.TryGetValue(typeName, out string? alias)
                    ? $"S1Interop.Generated.S1InteropTypeRegistry.{alias}"
                    : match.Value;
            });
    }

    private static bool CanRewriteSource(string source, IReadOnlyList<SdkTypeAlias> aliases) =>
        !string.Equals(source, RewriteSource(source, aliases), StringComparison.Ordinal);

    private static bool IsGeneratedFacade(string file) =>
        Path.GetFileName(file).Equals("S1Interop.GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase);
}
