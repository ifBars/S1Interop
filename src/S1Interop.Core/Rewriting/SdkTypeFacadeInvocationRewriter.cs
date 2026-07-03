using System.Text.RegularExpressions;

namespace S1Interop.Core;

public static class SdkTypeFacadeInvocationRewriter
{
    private static readonly Regex SimpleCreationRegex = new(
        @"(?<prefix>\bvar\s+(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)new\s+(?<type>(?:Il2Cpp)?ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*){2,})\s*\((?<args>[^;\r\n()]*)\)(?<suffix>\s*;)",
        RegexOptions.Compiled);

    private static readonly Regex ChainedInvocationRegex = new(
        @"new\s+(?<type>(?:Il2Cpp)?ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*){2,})\s*\((?<ctorArgs>[^;\r\n()]*)\)\s*\.\s*(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<methodArgs>[^;\r\n()]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex Il2CppListCreationRegex = new(
        @"new\s+Il2CppSystem\.Collections\.Generic\.List\s*<(?<type>[^;\r\n<>]+)>\s*\(",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> FindFilesWithRewritableInvocations(
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

        Dictionary<string, TypeFacadeTarget> targetsByRuntimeType = aliases
            .Select(alias => new TypeFacadeTarget(alias.MonoType, alias.Il2CppType, GetFacadeName(alias.MonoType)))
            .SelectMany(target => new[]
            {
                new KeyValuePair<string, TypeFacadeTarget>(target.MonoType, target),
                new KeyValuePair<string, TypeFacadeTarget>(target.Il2CppType, target)
            })
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.Value.FacadeName).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var variableFacades = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = RewriteIl2CppListCreation(lines[index]);
            line = RewriteChainedInvocations(line, targetsByRuntimeType);
            line = RewriteSimpleCreation(line, targetsByRuntimeType, variableFacades);
            line = RewriteMappedVariableInvocation(line, variableFacades);
            lines[index] = line;
        }

        return string.Join(newline, lines);
    }

    private static string RewriteIl2CppListCreation(string line) =>
        Il2CppListCreationRegex.Replace(line, match => $"new System.Collections.Generic.List<{match.Groups["type"].Value}>(");

    private static string RewriteChainedInvocations(
        string line,
        IReadOnlyDictionary<string, TypeFacadeTarget> targetsByRuntimeType)
    {
        return ChainedInvocationRegex.Replace(
            line,
            match =>
            {
                string runtimeTypeName = ToMonoTypeName(match.Groups["type"].Value);
                if (!targetsByRuntimeType.TryGetValue(runtimeTypeName, out TypeFacadeTarget? target))
                {
                    return match.Value;
                }

                string ctorArgs = match.Groups["ctorArgs"].Value.Trim();
                string methodName = match.Groups["method"].Value;
                string methodArgs = FormatInvocationArguments(match.Groups["methodArgs"].Value);
                string instance = string.IsNullOrWhiteSpace(ctorArgs)
                    ? $"{target.FacadeName}.Create()"
                    : $"{target.FacadeName}.Create({ctorArgs})";
                return string.IsNullOrWhiteSpace(methodArgs)
                    ? $"{target.FacadeName}.Invoke({instance}, \"{methodName}\")"
                    : $"{target.FacadeName}.Invoke({instance}, \"{methodName}\", {methodArgs})";
            });
    }

    private static string RewriteSimpleCreation(
        string line,
        IReadOnlyDictionary<string, TypeFacadeTarget> targetsByRuntimeType,
        Dictionary<string, string> variableFacades)
    {
        return SimpleCreationRegex.Replace(
            line,
            match =>
            {
                string runtimeTypeName = ToMonoTypeName(match.Groups["type"].Value);
                if (!targetsByRuntimeType.TryGetValue(runtimeTypeName, out TypeFacadeTarget? target))
                {
                    return match.Value;
                }

                string variableName = match.Groups["variable"].Value;
                variableFacades[variableName] = target.FacadeName;
                string args = match.Groups["args"].Value.Trim();
                string createCall = string.IsNullOrWhiteSpace(args)
                    ? $"{target.FacadeName}.Create()"
                    : $"{target.FacadeName}.Create({args})";
                return $"{match.Groups["prefix"].Value}{createCall}{match.Groups["suffix"].Value}";
            });
    }

    private static string RewriteMappedVariableInvocation(
        string line,
        IReadOnlyDictionary<string, string> variableFacades)
    {
        foreach ((string variableName, string facadeName) in variableFacades)
        {
            string pattern = $@"(?<![A-Za-z0-9_\.]){Regex.Escape(variableName)}\s*\.\s*(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>[^;\r\n()]*)\)";
            line = Regex.Replace(
                line,
                pattern,
                match =>
                {
                    string args = FormatInvocationArguments(match.Groups["args"].Value);
                    string methodName = match.Groups["method"].Value;
                    return string.IsNullOrWhiteSpace(args)
                        ? $"{facadeName}.Invoke({variableName}, \"{methodName}\")"
                        : $"{facadeName}.Invoke({variableName}, \"{methodName}\", {args})";
                });
        }

        return line;
    }

    private static string FormatInvocationArguments(string arguments)
    {
        string trimmed = arguments.Trim();
        return string.Equals(trimmed, "null", StringComparison.Ordinal)
            ? "(object?)null"
            : trimmed;
    }

    private static bool CanRewriteSource(string source, IReadOnlyList<SdkTypeAlias> aliases) =>
        !string.Equals(source, RewriteSource(source, aliases), StringComparison.Ordinal);

    private static string GetFacadeName(string monoTypeName)
    {
        string[] parts = monoTypeName
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeIdentifier)
            .ToArray();
        if (parts.Length == 0)
        {
            return "S1Interop.Type";
        }

        string typeName = parts[^1];
        IEnumerable<string> namespaceParts = parts.Take(parts.Length - 1);
        if (parts[0].Equals("ScheduleOne", StringComparison.Ordinal))
        {
            namespaceParts = namespaceParts.Skip(1);
        }
        else
        {
            namespaceParts = new[] { "Types" }.Concat(namespaceParts);
        }

        string namespaceSuffix = string.Join(".", namespaceParts);
        return string.IsNullOrWhiteSpace(namespaceSuffix)
            ? $"S1Interop.{typeName}"
            : $"S1Interop.{namespaceSuffix}.{typeName}";
    }

    private static string SanitizeIdentifier(string value)
    {
        var chars = value
            .Where(character => char.IsLetterOrDigit(character) || character == '_')
            .ToArray();
        string sanitized = new(chars);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Type";
        }

        return char.IsLetter(sanitized[0]) || sanitized[0] == '_'
            ? sanitized
            : "_" + sanitized;
    }

    private static string ToMonoTypeName(string type) =>
        type.StartsWith("Il2Cpp", StringComparison.Ordinal)
            ? type["Il2Cpp".Length..]
            : type;

    private static bool IsGeneratedFacade(string file) =>
        Path.GetFileName(file).Equals("S1Interop.GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase);

    private sealed record TypeFacadeTarget(string MonoType, string Il2CppType, string FacadeName);
}
