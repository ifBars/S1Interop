using System.Text.RegularExpressions;

namespace S1Interop.Core;

public sealed class MemberAccessTargetCatalog
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        WorkspaceTraversal.CommonExcludedDirectoryNames,
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex UsingRegex = new(
        @"^\s*using\s+(?<namespace>ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex TypeOfFieldOrPropertyRegex = new(
        @"typeof\s*\(\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\)\s*\.\s*Get(?<kind>Field|Property)\s*\(\s*""(?<member>[A-Za-z_][A-Za-z0-9_]*)""\s*(?:,|\))",
        RegexOptions.Compiled);

    public IReadOnlyList<MemberAccessTarget> Discover(string projectPath)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        string projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return Array.Empty<MemberAccessTarget>();
        }

        string[] sourceFiles = WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs", ExcludedDirectoryNames)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return CreateUniqueAliases(sourceFiles.SelectMany(DiscoverFileTargets).ToArray());
    }

    public IReadOnlyList<MemberAccessTarget> DiscoverFileTargets(string sourceFile)
    {
        if (!File.Exists(sourceFile))
        {
            return Array.Empty<MemberAccessTarget>();
        }

        string[] lines = File.ReadAllLines(sourceFile);
        Dictionary<string, string> scheduleOneUsings = DiscoverScheduleOneUsings(lines);
        var targets = new List<MemberAccessTarget>();

        for (int index = 0; index < lines.Length; index++)
        {
            Match match = TypeOfFieldOrPropertyRegex.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            string ownerTypeName = ResolveRuntimeTypeName(match.Groups["type"].Value, scheduleOneUsings);
            string ownerAlias = GetSimpleTypeName(ownerTypeName);
            string memberName = match.Groups["member"].Value;
            targets.Add(new MemberAccessTarget(
                Path.GetFullPath(sourceFile),
                index + 1,
                ownerAlias,
                ownerTypeName,
                memberName,
                SanitizeAlias(memberName),
                IsStatic: IsStaticMemberLookup(lines, index)));
        }

        return CreateUniqueAliases(targets);
    }

    private static bool IsStaticMemberLookup(IReadOnlyList<string> lines, int startIndex)
    {
        string statement = GetStatementWindow(lines, startIndex, maxLineCount: 4);
        return statement.Contains("BindingFlags.Static", StringComparison.Ordinal) &&
               !statement.Contains("BindingFlags.Instance", StringComparison.Ordinal);
    }

    private static string GetStatementWindow(IReadOnlyList<string> lines, int startIndex, int maxLineCount)
    {
        int endIndex = Math.Min(lines.Count, startIndex + maxLineCount);
        var statementLines = new List<string>();
        for (int index = startIndex; index < endIndex; index++)
        {
            statementLines.Add(lines[index]);
            if (lines[index].Contains(';', StringComparison.Ordinal))
            {
                break;
            }
        }

        return string.Join('\n', statementLines);
    }

    private static IReadOnlyList<MemberAccessTarget> CreateUniqueAliases(IReadOnlyList<MemberAccessTarget> targets)
    {
        var usedAliases = new Dictionary<string, int>(StringComparer.Ordinal);
        var results = new List<MemberAccessTarget>(targets.Count);

        foreach (MemberAccessTarget target in targets
                     .DistinctBy(target => (target.OwnerTypeName, target.MemberName, target.IsStatic))
                     .OrderBy(target => target.SourceFilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(target => target.Line))
        {
            string baseAlias = SanitizeAlias(target.MemberName);
            string alias = baseAlias;
            if (usedAliases.TryGetValue(baseAlias, out int count))
            {
                count++;
                usedAliases[baseAlias] = count;
                alias = $"{target.OwnerAlias}{baseAlias}{count}";
            }
            else
            {
                usedAliases[baseAlias] = 1;
            }

            results.Add(target with { MemberAlias = alias });
        }

        return results;
    }

    private static Dictionary<string, string> DiscoverScheduleOneUsings(IEnumerable<string> lines)
    {
        var usings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match match = UsingRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            string namespaceName = match.Groups["namespace"].Value;
            string leaf = namespaceName[(namespaceName.LastIndexOf('.') + 1)..];
            usings[leaf] = namespaceName;
        }

        return usings;
    }

    private static string ResolveRuntimeTypeName(
        string sourceType,
        IReadOnlyDictionary<string, string> scheduleOneUsings)
    {
        if (sourceType.StartsWith("ScheduleOne.", StringComparison.Ordinal))
        {
            return sourceType;
        }

        if (sourceType.Contains('.', StringComparison.Ordinal))
        {
            return sourceType;
        }

        foreach (KeyValuePair<string, string> @using in scheduleOneUsings)
        {
            if (string.Equals(@using.Key, sourceType, StringComparison.Ordinal))
            {
                return @using.Value;
            }
        }

        return sourceType;
    }

    private static string GetSimpleTypeName(string typeName)
    {
        int separator = typeName.LastIndexOf('.');
        return separator < 0 ? typeName : typeName[(separator + 1)..];
    }

    private static string SanitizeAlias(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Member";
        }

        var chars = value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
        string sanitized = new(chars);
        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
    }
}
