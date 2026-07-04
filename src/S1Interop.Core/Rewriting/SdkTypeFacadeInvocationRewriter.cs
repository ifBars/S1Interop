using System.Text;
using System.Text.RegularExpressions;

namespace S1Interop.Core;

public static class SdkTypeFacadeInvocationRewriter
{
    private enum RewriteState
    {
        Code,
        BlockComment,
        RegularString,
        VerbatimString,
        CharLiteral
    }

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
        RewriteState state = RewriteState.Code;

        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = RewriteLine(lines[index], targetsByRuntimeType, variableFacades, ref state);
        }

        return string.Join(newline, lines);
    }

    private static string RewriteLine(
        string line,
        IReadOnlyDictionary<string, TypeFacadeTarget> targetsByRuntimeType,
        Dictionary<string, string> variableFacades,
        ref RewriteState state)
    {
        var rewritten = new StringBuilder(line.Length);
        var code = new StringBuilder();

        for (int index = 0; index < line.Length; index++)
        {
            char ch = line[index];
            char next = index + 1 < line.Length ? line[index + 1] : '\0';

            switch (state)
            {
                case RewriteState.Code:
                    if (ch == '/' && next == '/')
                    {
                        AppendRewrittenCode(rewritten, code, targetsByRuntimeType, variableFacades);
                        rewritten.Append(line, index, line.Length - index);
                        index = line.Length;
                    }
                    else if (ch == '/' && next == '*')
                    {
                        AppendRewrittenCode(rewritten, code, targetsByRuntimeType, variableFacades);
                        rewritten.Append("/*");
                        index++;
                        state = RewriteState.BlockComment;
                    }
                    else if (ch == '"')
                    {
                        AppendRewrittenCode(rewritten, code, targetsByRuntimeType, variableFacades);
                        rewritten.Append(ch);
                        state = IsVerbatimStringStart(line, index)
                            ? RewriteState.VerbatimString
                            : RewriteState.RegularString;
                    }
                    else if (ch == '\'')
                    {
                        AppendRewrittenCode(rewritten, code, targetsByRuntimeType, variableFacades);
                        rewritten.Append(ch);
                        state = RewriteState.CharLiteral;
                    }
                    else
                    {
                        code.Append(ch);
                    }

                    break;

                case RewriteState.BlockComment:
                    rewritten.Append(ch);
                    if (ch == '*' && next == '/')
                    {
                        rewritten.Append(next);
                        index++;
                        state = RewriteState.Code;
                    }

                    break;

                case RewriteState.RegularString:
                    rewritten.Append(ch);
                    if (ch == '\\' && next != '\0')
                    {
                        rewritten.Append(next);
                        index++;
                    }
                    else if (ch == '"')
                    {
                        state = RewriteState.Code;
                    }

                    break;

                case RewriteState.VerbatimString:
                    rewritten.Append(ch);
                    if (ch == '"' && next == '"')
                    {
                        rewritten.Append(next);
                        index++;
                    }
                    else if (ch == '"')
                    {
                        state = RewriteState.Code;
                    }

                    break;

                case RewriteState.CharLiteral:
                    rewritten.Append(ch);
                    if (ch == '\\' && next != '\0')
                    {
                        rewritten.Append(next);
                        index++;
                    }
                    else if (ch == '\'')
                    {
                        state = RewriteState.Code;
                    }

                    break;
            }
        }

        AppendRewrittenCode(rewritten, code, targetsByRuntimeType, variableFacades);
        return rewritten.ToString();
    }

    private static void AppendRewrittenCode(
        StringBuilder rewritten,
        StringBuilder code,
        IReadOnlyDictionary<string, TypeFacadeTarget> targetsByRuntimeType,
        Dictionary<string, string> variableFacades)
    {
        if (code.Length == 0)
        {
            return;
        }

        string segment = RewriteIl2CppListCreation(code.ToString());
        segment = RewriteChainedInvocations(segment, targetsByRuntimeType);
        segment = RewriteSimpleCreation(segment, targetsByRuntimeType, variableFacades);
        segment = RewriteMappedVariableInvocation(segment, variableFacades);
        rewritten.Append(segment);
        code.Clear();
    }

    private static bool IsVerbatimStringStart(string line, int quoteIndex)
    {
        int previous = quoteIndex - 1;
        if (previous < 0)
        {
            return false;
        }

        if (line[previous] == '@')
        {
            return true;
        }

        return line[previous] == '$' && previous - 1 >= 0 && line[previous - 1] == '@';
    }

    private static string RewriteIl2CppListCreation(string line)
    {
        return Il2CppListCreationRegex.Replace(
            line,
            match => IsExplicitIl2CppListDeclaration(line, match.Index)
                ? match.Value
                : $"new System.Collections.Generic.List<{match.Groups["type"].Value}>(");
    }

    private static bool IsExplicitIl2CppListDeclaration(string line, int creationIndex)
    {
        int statementStart = Math.Max(line.LastIndexOf(';', Math.Max(0, creationIndex - 1)), line.LastIndexOf('{', Math.Max(0, creationIndex - 1))) + 1;
        ReadOnlySpan<char> prefix = line.AsSpan(statementStart, creationIndex - statementStart);
        return prefix.Contains("Il2CppSystem.Collections.Generic.List".AsSpan(), StringComparison.Ordinal) &&
               prefix.Contains("=".AsSpan(), StringComparison.Ordinal);
    }

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
