using System.Text.RegularExpressions;

namespace S1Interop.Core.Analysis;

/// <summary>
/// Discovers simple Harmony method lookups that can move to generated S1Interop method declarations.
/// </summary>
public sealed class HarmonyMethodTargetCatalog
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        WorkspaceTraversal.CommonExcludedDirectoryNames,
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex MethodStartRegex = new(
        @"^(?<indent>\s*)(?:(?:var|(?:System\.Reflection\.)?MethodInfo\??)\s+)?(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*AccessTools\.Method\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex TypeOfGetMethodStartRegex = new(
        @"^(?<indent>\s*)(?:(?:var|(?:System\.Reflection\.)?MethodInfo\??)\s+)?(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*typeof\s*\(\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\)\s*\.\s*GetMethod\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex UsingRegex = new(
        @"^\s*using\s+(?<namespace>ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex UsingAliasRegex = new(
        @"^\s*using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<type>(?:Il2Cpp)?ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex TypeOfRegex = new(
        @"typeof\s*\(\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex NameOfMethodRegex = new(
        @"nameof\s*\(\s*(?:(?:[A-Za-z_][A-Za-z0-9_.]*)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex QuotedMethodRegex = new(
        @"""(?<name>[A-Za-z_][A-Za-z0-9_]*)""",
        RegexOptions.Compiled);

    private static readonly HashSet<string> PrimitiveTypeNames = new(StringComparer.Ordinal)
    {
        "bool",
        "byte",
        "char",
        "double",
        "float",
        "int",
        "long",
        "object",
        "short",
        "string",
        "uint",
        "ulong",
        "void"
    };

    /// <summary>
    /// Discovers supported Harmony method targets across a project's C# source files.
    /// </summary>
    /// <param name="projectPath">The path to the owning <c>.csproj</c> file.</param>
    /// <returns>Unique method targets with generated owner and method aliases.</returns>
    public IReadOnlyList<HarmonyMethodTarget> Discover(string projectPath)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        string projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return Array.Empty<HarmonyMethodTarget>();
        }

        string[] sourceFiles = WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs", ExcludedDirectoryNames)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return CreateUniqueAliases(sourceFiles.SelectMany(DiscoverFileTargets).ToArray());
    }

    /// <summary>
    /// Discovers supported Harmony method targets in one C# source file.
    /// </summary>
    /// <param name="sourceFile">The C# source file to inspect.</param>
    /// <returns>The method targets found in the file, or an empty list when the file does not exist.</returns>
    public IReadOnlyList<HarmonyMethodTarget> DiscoverFileTargets(string sourceFile)
    {
        if (!File.Exists(sourceFile))
        {
            return Array.Empty<HarmonyMethodTarget>();
        }

        string[] lines = File.ReadAllLines(sourceFile);
        Dictionary<string, string> scheduleOneUsings = DiscoverScheduleOneUsings(lines);
        var targets = new List<HarmonyMethodTarget>();
        for (int index = 0; index < lines.Length; index++)
        {
            Match startMatch = MethodStartRegex.Match(lines[index]);
            Match typeOfGetMethodStartMatch = TypeOfGetMethodStartRegex.Match(lines[index]);
            if (!startMatch.Success && !typeOfGetMethodStartMatch.Success)
            {
                continue;
            }

            if (!TryReadInvocationBlock(lines, index, out int endLine, out string block))
            {
                continue;
            }

            MatchCollection typeMatches = TypeOfRegex.Matches(block);
            if (typeMatches.Count < 1)
            {
                continue;
            }

            string ownerType = typeMatches[0].Groups["type"].Value;
            string ownerTypeName = ResolveRuntimeTypeName(ownerType, scheduleOneUsings);

            string? methodName = TryGetMethodName(block);
            if (methodName is null)
            {
                continue;
            }

            var parameterTypeNames = new List<string>();
            for (int parameterIndex = 1; parameterIndex < typeMatches.Count; parameterIndex++)
            {
                Match typeMatch = typeMatches[parameterIndex];
                string parameterType = typeMatch.Groups["type"].Value;
                string parameterTypeName = ResolveParameterTypeName(parameterType, scheduleOneUsings);

                int segmentEnd = parameterIndex + 1 < typeMatches.Count
                    ? typeMatches[parameterIndex + 1].Index
                    : block.Length;
                string segment = block[typeMatch.Index..segmentEnd];
                if (segment.Contains(".MakeByRefType()", StringComparison.Ordinal))
                {
                    parameterTypeName += "&";
                }

                parameterTypeNames.Add(parameterTypeName);
            }

            string ownerAlias = GetSimpleTypeName(ownerTypeName);
            string variableName = startMatch.Success
                ? startMatch.Groups["variable"].Value
                : typeOfGetMethodStartMatch.Groups["variable"].Value;
            targets.Add(new HarmonyMethodTarget(
                Path.GetFullPath(sourceFile),
                index + 1,
                endLine + 1,
                variableName,
                ownerAlias,
                ownerTypeName,
                methodName,
                SanitizeAlias(methodName),
                parameterTypeNames));
        }

        return CreateUniqueAliases(targets);
    }

    private static IReadOnlyList<HarmonyMethodTarget> CreateUniqueAliases(IReadOnlyList<HarmonyMethodTarget> targets)
    {
        var usedAliases = new Dictionary<string, int>(StringComparer.Ordinal);
        var results = new List<HarmonyMethodTarget>(targets.Count);
        foreach (HarmonyMethodTarget target in targets
                     .OrderBy(target => target.SourceFilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(target => target.StartLine))
        {
            string baseAlias = SanitizeAlias(target.MethodName);
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

            results.Add(target with { MethodAlias = alias });
        }

        return results;
    }

    private static Dictionary<string, string> DiscoverScheduleOneUsings(IEnumerable<string> lines)
    {
        var usings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match aliasMatch = UsingAliasRegex.Match(line);
            if (aliasMatch.Success)
            {
                usings[aliasMatch.Groups["alias"].Value] = NormalizeScheduleOneTypeName(aliasMatch.Groups["type"].Value);
                continue;
            }

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

    private static string NormalizeScheduleOneTypeName(string typeName) =>
        typeName.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal)
            ? typeName["Il2Cpp".Length..]
            : typeName;

    private static bool TryReadInvocationBlock(IReadOnlyList<string> lines, int startLine, out int endLine, out string block)
    {
        var parts = new List<string>();
        int depth = 0;
        bool started = false;
        for (int index = startLine; index < Math.Min(lines.Count, startLine + 24); index++)
        {
            string line = lines[index];
            parts.Add(line);
            foreach (char ch in line)
            {
                if (ch == '(')
                {
                    started = true;
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                }
            }

            if (started && depth <= 0 && line.Contains(';', StringComparison.Ordinal))
            {
                endLine = index;
                block = string.Join('\n', parts);
                return true;
            }
        }

        endLine = startLine;
        block = string.Empty;
        return false;
    }

    private static string? TryGetMethodName(string block)
    {
        Match match = NameOfMethodRegex.Match(block);
        if (match.Success)
        {
            return match.Groups["name"].Value;
        }

        match = QuotedMethodRegex.Match(block);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string ResolveParameterTypeName(
        string sourceType,
        IReadOnlyDictionary<string, string> scheduleOneUsings)
    {
        if (PrimitiveTypeNames.Contains(sourceType))
        {
            return sourceType;
        }

        return ResolveRuntimeTypeName(sourceType, scheduleOneUsings);
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

        string simpleName = GetSimpleTypeName(sourceType);
        if (sourceType.Contains('.', StringComparison.Ordinal))
        {
            string qualifier = sourceType[..sourceType.LastIndexOf('.')];
            if (scheduleOneUsings.TryGetValue(qualifier, out string? namespaceName))
            {
                return $"{namespaceName}.{simpleName}";
            }
        }

        if (scheduleOneUsings.TryGetValue(sourceType, out string? typeName))
        {
            return typeName;
        }

        return TryInferNamespaceByLeaf(sourceType, scheduleOneUsings.Values, out string? inferredNamespaceName)
            ? $"{inferredNamespaceName}.{sourceType}"
            : simpleName;
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

    private static string GetSimpleTypeName(string typeName) =>
        typeName[(typeName.LastIndexOf('.') + 1)..];

    private static string SanitizeAlias(string value)
    {
        string sanitized = Regex.Replace(value, @"[^A-Za-z0-9_]", "_");
        return sanitized.Length > 0 && char.IsDigit(sanitized[0])
            ? "_" + sanitized
            : sanitized;
    }
}
