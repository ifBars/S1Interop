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

    private static readonly Regex InstanceGetTypeFieldOrPropertyRegex = new(
        @"(?<receiver>[_A-Za-z][A-Za-z0-9_]*)\s*\.\s*GetType\s*\(\s*\)\s*\.\s*Get(?<kind>Field|Property)\s*\(\s*""(?<member>[A-Za-z_][A-Za-z0-9_]*)""\s*(?:,|\))",
        RegexOptions.Compiled);

    private static readonly Regex FieldOrPropertyHelperRegex = new(
        @"(?<helper>[A-Za-z_][A-Za-z0-9_.]*)\s*\.\s*Try(?<operation>Get|Set)(?<static>Static)?FieldOrProperty\s*\(\s*(?<target>[_A-Za-z][A-Za-z0-9_]*)\s*,\s*""(?<member>[A-Za-z_][A-Za-z0-9_]*)""",
        RegexOptions.Compiled);

    private static readonly Regex GeneratedTypeAttributeRegex = new(
        @"S1InteropType\s*\(\s*""(?<type>[^""]+)""\s*,\s*Alias\s*=\s*""(?<alias>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex GeneratedMemberAttributeRegex = new(
        @"S1InteropMember\s*\(\s*""(?<owner>[^""]+)""\s*,\s*""(?<member>[^""]+)""\s*,\s*Alias\s*=\s*""(?<alias>[^""]+)""(?<options>[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex IdentifierTypeRegex = new(
        @"(?<![A-Za-z0-9_])(?:(?:public|private|protected|internal|static|readonly|volatile|new|sealed|unsafe)\s+)*(?<type>[A-Za-z_][A-Za-z0-9_.]*)(?:\?)?(?:\s*<[^>\r\n;()]+>)?\s+(?<name>[_A-Za-z][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredTypeNames = new(StringComparer.Ordinal)
    {
        "bool",
        "byte",
        "char",
        "const",
        "decimal",
        "double",
        "dynamic",
        "float",
        "int",
        "long",
        "object",
        "sbyte",
        "short",
        "string",
        "uint",
        "ulong",
        "ushort",
        "var",
        "void"
    };

    private static readonly string[] IgnoredOwnerTypePrefixes =
    [
        "Melon",
        "MelonLoader."
    ];

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
        MemberAccessTarget[] sourceTargets = sourceFiles
            .SelectMany(DiscoverFileTargets)
            .Concat(DiscoverGeneratedTargets(projectDirectory))
            .ToArray();
        return CreateUniqueAliases(sourceTargets);
    }

    public IReadOnlyList<MemberAccessTarget> DiscoverFileTargets(string sourceFile)
    {
        if (!File.Exists(sourceFile))
        {
            return Array.Empty<MemberAccessTarget>();
        }

        string[] lines = File.ReadAllLines(sourceFile);
        Dictionary<string, string> scheduleOneUsings = DiscoverScheduleOneUsings(lines);
        string[] scheduleOneNamespaces = DiscoverScheduleOneNamespaces(scheduleOneUsings);
        Dictionary<string, string> identifierTypes = DiscoverIdentifierTypes(lines, scheduleOneUsings, scheduleOneNamespaces);
        var targets = new List<MemberAccessTarget>();

        for (int index = 0; index < lines.Length; index++)
        {
            Match match = TypeOfFieldOrPropertyRegex.Match(lines[index]);
            if (match.Success)
            {
                string ownerTypeName = ResolveRuntimeTypeName(match.Groups["type"].Value, scheduleOneUsings);
                if (IsIgnoredOwnerTypeName(ownerTypeName))
                {
                    continue;
                }

                string ownerAlias = GetSimpleTypeName(ownerTypeName);
                string memberName = match.Groups["member"].Value;
                targets.Add(new MemberAccessTarget(
                    Path.GetFullPath(sourceFile),
                    index + 1,
                    ownerAlias,
                    ownerTypeName,
                    memberName,
                    SanitizeAlias(memberName),
                    IsStatic: IsStaticMemberLookup(lines, index),
                    Kind: GetMemberAccessKind(match.Groups["kind"].Value)));
            }

            foreach (Match instanceMatch in InstanceGetTypeFieldOrPropertyRegex.Matches(lines[index]))
            {
                string receiver = instanceMatch.Groups["receiver"].Value;
                if (!identifierTypes.TryGetValue(receiver, out string? ownerTypeName))
                {
                    continue;
                }

                string ownerAlias = GetSimpleTypeName(ownerTypeName);
                string memberName = instanceMatch.Groups["member"].Value;
                targets.Add(new MemberAccessTarget(
                    Path.GetFullPath(sourceFile),
                    index + 1,
                    ownerAlias,
                    ownerTypeName,
                    memberName,
                    SanitizeAlias(memberName),
                    IsStatic: false,
                    Kind: GetMemberAccessKind(instanceMatch.Groups["kind"].Value)));
            }

            foreach (MemberAccessTarget target in DiscoverHelperTargets(sourceFile, lines[index], index + 1, identifierTypes))
            {
                targets.Add(target);
            }
        }

        return CreateUniqueAliases(targets);
    }

    private static MemberAccessKind GetMemberAccessKind(string kind) =>
        kind.Equals("Field", StringComparison.Ordinal)
            ? MemberAccessKind.Field
            : MemberAccessKind.Property;

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
                     .GroupBy(target => (target.OwnerTypeName, target.MemberName, target.IsStatic))
                     .Select(group => MergeMemberAccessKinds(group.ToArray()))
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

    private static MemberAccessTarget MergeMemberAccessKinds(IReadOnlyList<MemberAccessTarget> targets)
    {
        MemberAccessTarget first = targets
            .OrderBy(target => IsGeneratedMemberAccessTargetsFile(target.SourceFilePath))
            .ThenBy(target => target.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Line)
            .First();
        MemberAccessKind kind = targets.Select(target => target.Kind).Distinct().Count() == 1
            ? first.Kind
            : MemberAccessKind.FieldOrProperty;
        return first with { Kind = kind };
    }

    private static bool IsGeneratedMemberAccessTargetsFile(string path) =>
        Path.GetFileName(path).Equals(Generators.MemberAccessTargetGenerator.SourceFileName, StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty).Equals("S1Interop.Generated", StringComparison.OrdinalIgnoreCase);

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

    private static string[] DiscoverScheduleOneNamespaces(IReadOnlyDictionary<string, string> scheduleOneUsings)
    {
        return scheduleOneUsings
            .Values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, string> DiscoverIdentifierTypes(
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, string> scheduleOneUsings,
        IReadOnlyList<string> scheduleOneNamespaces)
    {
        var identifierTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            string code = StripLineComment(line);
            foreach (Match match in IdentifierTypeRegex.Matches(code))
            {
                string typeName = match.Groups["type"].Value;
                if (!CanResolveHelperTargetType(typeName, scheduleOneUsings))
                {
                    continue;
                }

                string identifier = match.Groups["name"].Value;
                identifierTypes[identifier] = ResolveHelperTargetTypeName(typeName, scheduleOneUsings, scheduleOneNamespaces);
            }
        }

        return identifierTypes;
    }

    private static IEnumerable<MemberAccessTarget> DiscoverHelperTargets(
        string sourceFile,
        string line,
        int lineNumber,
        IReadOnlyDictionary<string, string> identifierTypes)
    {
        foreach (Match match in FieldOrPropertyHelperRegex.Matches(line))
        {
            string targetName = match.Groups["target"].Value;
            if (!identifierTypes.TryGetValue(targetName, out string? ownerTypeName))
            {
                continue;
            }

            string ownerAlias = GetSimpleTypeName(ownerTypeName);
            string memberName = match.Groups["member"].Value;
            yield return new MemberAccessTarget(
                Path.GetFullPath(sourceFile),
                lineNumber,
                ownerAlias,
                ownerTypeName,
                memberName,
                SanitizeAlias(memberName),
                IsStatic: match.Groups["static"].Success,
                Kind: MemberAccessKind.FieldOrProperty);
        }
    }

    private static IEnumerable<MemberAccessTarget> DiscoverGeneratedTargets(string projectDirectory)
    {
        string generatedPath = Path.Combine(
            projectDirectory,
            "S1Interop.Generated",
            Generators.MemberAccessTargetGenerator.SourceFileName);
        if (!File.Exists(generatedPath))
        {
            yield break;
        }

        string[] lines = File.ReadAllLines(generatedPath);
        Dictionary<string, string> typeAliases = DiscoverGeneratedTypeAliases(lines);
        for (int index = 0; index < lines.Length; index++)
        {
            Match match = GeneratedMemberAttributeRegex.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            string ownerAlias = Unescape(match.Groups["owner"].Value);
            string memberName = Unescape(match.Groups["member"].Value);
            string memberAlias = Unescape(match.Groups["alias"].Value);
            string options = match.Groups["options"].Value;
            yield return new MemberAccessTarget(
                Path.GetFullPath(generatedPath),
                index + 1,
                ownerAlias,
                typeAliases.TryGetValue(ownerAlias, out string? ownerTypeName) ? ownerTypeName : ownerAlias,
                memberName,
                memberAlias,
                IsStatic: options.Contains("IsStatic = true", StringComparison.Ordinal),
                Kind: GetGeneratedMemberKind(options));
        }
    }

    private static Dictionary<string, string> DiscoverGeneratedTypeAliases(IEnumerable<string> lines)
    {
        var typeAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match match = GeneratedTypeAttributeRegex.Match(line);
            if (match.Success)
            {
                typeAliases[Unescape(match.Groups["alias"].Value)] = Unescape(match.Groups["type"].Value);
            }
        }

        return typeAliases;
    }

    private static MemberAccessKind GetGeneratedMemberKind(string options)
    {
        if (options.Contains("S1InteropMemberKind.Field", StringComparison.Ordinal))
        {
            return MemberAccessKind.Field;
        }

        return options.Contains("S1InteropMemberKind.Property", StringComparison.Ordinal)
            ? MemberAccessKind.Property
            : MemberAccessKind.FieldOrProperty;
    }

    private static bool CanResolveHelperTargetType(
        string typeName,
        IReadOnlyDictionary<string, string> scheduleOneUsings)
    {
        if (IgnoredTypeNames.Contains(typeName) ||
            typeName.StartsWith("System.", StringComparison.Ordinal))
        {
            return false;
        }

        return typeName.StartsWith("ScheduleOne.", StringComparison.Ordinal) ||
               typeName.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal) ||
               scheduleOneUsings.ContainsKey(typeName) ||
               scheduleOneUsings.Count == 1 ||
               TryInferNamespaceByLeaf(typeName, scheduleOneUsings.Values, out _);
    }

    private static string StripLineComment(string line)
    {
        int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return commentIndex < 0 ? line : line[..commentIndex];
    }

    private static string ResolveRuntimeTypeName(
        string sourceType,
        IReadOnlyDictionary<string, string> scheduleOneUsings)
    {
        if (sourceType.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal))
        {
            return sourceType["Il2Cpp".Length..];
        }

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

    private static string ResolveHelperTargetTypeName(
        string sourceType,
        IReadOnlyDictionary<string, string> scheduleOneUsings,
        IReadOnlyList<string> scheduleOneNamespaces)
    {
        string resolved = ResolveRuntimeTypeName(sourceType, scheduleOneUsings);
        if (!string.Equals(resolved, sourceType, StringComparison.Ordinal) ||
            sourceType.StartsWith("ScheduleOne.", StringComparison.Ordinal) ||
            sourceType.Contains('.', StringComparison.Ordinal))
        {
            return resolved;
        }

        if (scheduleOneNamespaces.Count == 1)
        {
            return $"{scheduleOneNamespaces[0]}.{sourceType}";
        }

        return TryInferNamespaceByLeaf(sourceType, scheduleOneNamespaces, out string? namespaceName)
            ? $"{namespaceName}.{sourceType}"
            : sourceType;
    }

    private static bool TryInferNamespaceByLeaf(
        string typeName,
        IEnumerable<string> namespaceNames,
        out string? namespaceName)
    {
        namespaceName = null;
        string[] matches = namespaceNames
            .Where(candidate => NamespaceLeafMatchesType(candidate, typeName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        namespaceName = matches[0];
        return true;
    }

    private static bool NamespaceLeafMatchesType(string namespaceName, string typeName)
    {
        string leaf = namespaceName[(namespaceName.LastIndexOf('.') + 1)..];
        if (typeName.StartsWith(leaf, StringComparison.Ordinal) ||
            typeName.EndsWith(leaf, StringComparison.Ordinal))
        {
            return true;
        }

        if (leaf.EndsWith("s", StringComparison.Ordinal))
        {
            string singular = leaf[..^1];
            return singular.Length > 2 &&
                   (typeName.StartsWith(singular, StringComparison.Ordinal) ||
                    typeName.EndsWith(singular, StringComparison.Ordinal));
        }

        const string scriptsSuffix = "Scripts";
        if (leaf.EndsWith(scriptsSuffix, StringComparison.Ordinal))
        {
            string prefix = leaf[..^scriptsSuffix.Length];
            return prefix.Length > 2 && typeName.StartsWith(prefix, StringComparison.Ordinal);
        }

        return false;
    }

    private static string GetSimpleTypeName(string typeName)
    {
        int separator = typeName.LastIndexOf('.');
        return separator < 0 ? typeName : typeName[(separator + 1)..];
    }

    private static bool IsIgnoredOwnerTypeName(string typeName)
    {
        string normalized = typeName.Trim();
        string simpleName = GetSimpleTypeName(normalized);
        return IgnoredOwnerTypePrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            simpleName.StartsWith(prefix, StringComparison.Ordinal));
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

    private static string Unescape(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
}
