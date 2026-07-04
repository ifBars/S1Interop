using System.Text.RegularExpressions;

namespace S1Interop.Core;

public sealed class MemberAccessFallbackRewriter
{
    private static readonly Regex DynamicInstanceLookupPattern = new(
        @"(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*GetType\s*\(\s*\)\s*\.\s*Get(?<kind>Field|Property)\s*\(\s*""(?<member>[A-Za-z_][A-Za-z0-9_]*)""\s*(?:,|\))",
        RegexOptions.Compiled);

    private static readonly Regex TypedHelperLookupPattern = new(
        @"(?<call>[A-Za-z_][A-Za-z0-9_.]*\s*\.\s*Try(?<operation>Get|Set)FieldOrProperty\s*\(\s*(?<target>[_A-Za-z][A-Za-z0-9_]*)\s*,\s*""(?<member>[A-Za-z_][A-Za-z0-9_]*)""(?:\s*,\s*(?<value>[^;\r\n()]+))?\s*\))",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ValueReturnTypes = new(StringComparer.Ordinal)
    {
        "bool",
        "byte",
        "char",
        "decimal",
        "double",
        "float",
        "int",
        "long",
        "short",
        "uint",
        "ulong"
    };

    private static readonly HashSet<string> UnsupportedReturnTypes = new(StringComparer.Ordinal)
    {
        "object",
        "void"
    };

    private static readonly HashSet<string> DynamicFallbackReturnTypes = new(StringComparer.Ordinal)
    {
        "object",
        "object?"
    };

    private static readonly HashSet<string> MethodModifiers = new(StringComparer.Ordinal)
    {
        "async",
        "extern",
        "internal",
        "new",
        "override",
        "private",
        "protected",
        "public",
        "readonly",
        "sealed",
        "static",
        "unsafe",
        "virtual"
    };

    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("FieldPropertyReflectionFallback", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(risk.FilePath))
        {
            return false;
        }

        MemberAccessTarget? target = new MemberAccessTargetCatalog()
            .DiscoverFileTargets(risk.FilePath)
            .FirstOrDefault(target => target.Line == risk.Line);
        string source = File.ReadAllText(risk.FilePath);
        if (target is not null && new MemberAccessFallbackRewriter().RewriteSource(source, risk.FilePath, [target]) != source)
        {
            return true;
        }

        return CanRewriteDynamicInstanceFallback(source, risk.Line) ||
               CanRewriteDynamicHelperMethod(source, risk.Line);
    }

    public string RewriteSource(string source, string sourcePath, IReadOnlyList<MemberAccessTarget> projectTargets)
    {
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = source.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  source.EndsWith('\n');
        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (hadTrailingNewline && lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        MemberAccessTarget[] sourceTargets = projectTargets
            .Where(target => string.Equals(Path.GetFullPath(target.SourceFilePath), fullSourcePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(target => target.Line)
            .ToArray();
        if (sourceTargets.Length == 0 && projectTargets.Count == 1)
        {
            sourceTargets = projectTargets.OrderByDescending(target => target.Line).ToArray();
        }

        var rewrittenLines = lines.ToList();
        bool changed = false;
        foreach (MemberAccessTarget target in sourceTargets)
        {
            if (!TryRewriteTarget(rewrittenLines, target) &&
                !TryRewriteTypedHelperCall(rewrittenLines, target))
            {
                continue;
            }

            changed = true;
        }

        changed |= TryRewriteDynamicHelperMethods(rewrittenLines);
        changed |= TryRewriteDynamicInstanceFallbacks(rewrittenLines);

        if (!changed)
        {
            return source;
        }

        string rewritten = string.Join(newline, rewrittenLines);
        return hadTrailingNewline ? rewritten + newline : rewritten;
    }

    private static bool TryRewriteTarget(List<string> lines, MemberAccessTarget target)
    {
        int memberLine = FindTypedMemberAccessLine(lines, target);
        if (memberLine < 0 ||
            !TryFindContainingMethod(lines, memberLine, out int methodLine, out MethodSignature signature) ||
            !TryFindMethodBody(lines, methodLine, out int openBraceLine, out int closeBraceLine) ||
            !CanRewriteBody(lines, openBraceLine + 1, closeBraceLine - 1, target, signature.ParameterName))
        {
            return false;
        }

        bool nullableReturn = signature.ReturnType.EndsWith("?", StringComparison.Ordinal);
        string castType = nullableReturn
            ? signature.ReturnType[..^1]
            : signature.ReturnType;
        if (UnsupportedReturnTypes.Contains(castType))
        {
            return false;
        }

        if (ValueReturnTypes.Contains(castType) && !nullableReturn)
        {
            return false;
        }

        string memberName = ToPascalIdentifier(target.MemberName);
        string accessor = ValueReturnTypes.Contains(castType)
            ? $"Get{memberName}Value<{castType}>"
            : $"Get{memberName}<{castType}>";
        string replacement = $"{signature.Indent}    return {BuildTypeFacadeAccessor(target, accessor, target.IsStatic ? string.Empty : signature.ParameterName)};";
        lines.RemoveRange(openBraceLine + 1, closeBraceLine - openBraceLine - 1);
        lines.Insert(openBraceLine + 1, replacement);
        return true;
    }

    private static bool TryRewriteTypedHelperCall(List<string> lines, MemberAccessTarget target)
    {
        int lineIndex = target.Line - 1;
        if (lineIndex < 0 || lineIndex >= lines.Count)
        {
            return false;
        }

        string line = lines[lineIndex];
        Match match = TypedHelperLookupPattern.Match(line);
        if (!match.Success ||
            !match.Groups["member"].Value.Equals(target.MemberName, StringComparison.Ordinal))
        {
            return false;
        }

        string replacement = match.Groups["operation"].Value.Equals("Set", StringComparison.Ordinal)
            ? BuildTypedHelperSetterReplacement(match, target)
            : BuildTypedHelperGetterReplacement(match, target);
        if (replacement.Length == 0)
        {
            return false;
        }

        lines[lineIndex] = line[..match.Groups["call"].Index] + replacement + line[(match.Groups["call"].Index + match.Groups["call"].Length)..];
        return true;
    }

    private static string BuildTypedHelperGetterReplacement(Match match, MemberAccessTarget target)
    {
        if (target.IsStatic)
        {
            return $"{GetTypeFacadeName(target)}.Get{ToPascalIdentifier(target.MemberName)}()";
        }

        string instance = match.Groups["target"].Value;
        return $"{GetTypeFacadeName(target)}.Get{ToPascalIdentifier(target.MemberName)}({instance})";
    }

    private static string BuildTypedHelperSetterReplacement(Match match, MemberAccessTarget target)
    {
        if (!match.Groups["value"].Success)
        {
            return string.Empty;
        }

        string value = match.Groups["value"].Value.Trim();
        if (target.IsStatic)
        {
            return $"{GetTypeFacadeName(target)}.TrySet{ToPascalIdentifier(target.MemberName)}({value})";
        }

        string instance = match.Groups["target"].Value;
        return $"{GetTypeFacadeName(target)}.TrySet{ToPascalIdentifier(target.MemberName)}({instance}, {value})";
    }

    private static string BuildTypeFacadeAccessor(MemberAccessTarget target, string accessor, string instance)
    {
        string facadeName = GetTypeFacadeName(target);
        return string.IsNullOrWhiteSpace(instance)
            ? $"{facadeName}.{accessor}()"
            : $"{facadeName}.{accessor}({instance})";
    }

    private static string GetTypeFacadeName(MemberAccessTarget target)
    {
        string[] parts = target.OwnerTypeName
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeIdentifier)
            .ToArray();
        if (parts.Length == 0)
        {
            return $"S1Interop.{SanitizeIdentifier(target.OwnerAlias)}";
        }

        string typeName = parts[^1];
        IEnumerable<string> namespaceParts = parts.Take(parts.Length - 1);

        string namespaceSuffix = string.Join(".", namespaceParts);
        return string.IsNullOrWhiteSpace(namespaceSuffix)
            ? $"S1Interop.{typeName}"
            : $"S1Interop.{namespaceSuffix}.{typeName}";
    }

    private static string ToPascalIdentifier(string value)
    {
        string sanitized = SanitizeIdentifier(value);
        if (sanitized.Length == 1)
        {
            return sanitized.ToUpperInvariant();
        }

        return char.ToUpperInvariant(sanitized[0]) + sanitized[1..];
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "RuntimeType";
        }

        var chars = value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
        string sanitized = new(chars);
        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
    }

    private static bool TryRewriteDynamicInstanceFallbacks(List<string> lines)
    {
        bool changed = false;
        for (int index = lines.Count - 1; index >= 0; index--)
        {
            if (!ContainsDynamicInstanceLookup(lines[index]) ||
                !TryFindContainingMethod(lines, index, out int methodLine, out MethodSignature signature) ||
                !DynamicFallbackReturnTypes.Contains(signature.ReturnType) ||
                !TryFindMethodBody(lines, methodLine, out int openBraceLine, out int closeBraceLine) ||
                !TryFindDynamicInstanceFallback(lines, openBraceLine + 1, closeBraceLine - 1, out string parameterName, out string memberName))
            {
                continue;
            }

            string replacement = $"{signature.Indent}    return S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue({parameterName}, \"{memberName}\");";
            lines.RemoveRange(openBraceLine + 1, closeBraceLine - openBraceLine - 1);
            lines.Insert(openBraceLine + 1, replacement);
            changed = true;
            index = methodLine;
        }

        return changed;
    }

    private static bool TryRewriteDynamicHelperMethods(List<string> lines)
    {
        bool changed = false;
        for (int index = lines.Count - 1; index >= 0; index--)
        {
            if (!TryParseDynamicHelperSignature(lines[index], out DynamicHelperSignature signature) ||
                !TryFindMethodBody(lines, index, out int openBraceLine, out int closeBraceLine) ||
                !CanRewriteDynamicHelperBody(lines, openBraceLine + 1, closeBraceLine - 1, signature))
            {
                continue;
            }

            string replacement = signature.Kind == DynamicHelperKind.Setter
                ? $"{signature.Indent}    return S1Interop.Generated.S1InteropMemberRegistry.TrySetInstanceValue({signature.TargetParameterName}, {signature.MemberParameterName}, {signature.ValueParameterName});"
                : $"{signature.Indent}    return S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue({signature.TargetParameterName}, {signature.MemberParameterName});";
            lines.RemoveRange(openBraceLine + 1, closeBraceLine - openBraceLine - 1);
            lines.Insert(openBraceLine + 1, replacement);
            changed = true;
        }

        return changed;
    }

    private static bool TryParseDynamicHelperSignature(string line, out DynamicHelperSignature signature)
    {
        signature = default;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.Contains("=>", StringComparison.Ordinal) ||
            !trimmed.Contains('(', StringComparison.Ordinal) ||
            !trimmed.Contains(')', StringComparison.Ordinal))
        {
            return false;
        }

        int openParen = trimmed.IndexOf('(');
        int closeParen = trimmed.IndexOf(')', openParen + 1);
        if (openParen <= 0 || closeParen <= openParen)
        {
            return false;
        }

        string beforeParen = trimmed[..openParen].Trim();
        string[] beforeTokens = beforeParen.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (beforeTokens.Length < 2 || beforeTokens.Any(token => token.Contains('.', StringComparison.Ordinal)))
        {
            return false;
        }

        string returnType = beforeTokens[^2];
        DynamicHelperKind kind;
        if (DynamicFallbackReturnTypes.Contains(returnType))
        {
            kind = DynamicHelperKind.Getter;
        }
        else if (returnType.Equals("bool", StringComparison.Ordinal))
        {
            kind = DynamicHelperKind.Setter;
        }
        else
        {
            return false;
        }

        string[] parameters = trimmed[(openParen + 1)..closeParen]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (kind == DynamicHelperKind.Getter && parameters.Length != 2 ||
            kind == DynamicHelperKind.Setter && parameters.Length != 3)
        {
            return false;
        }

        if (!TryParseParameter(parameters[0], out string targetType, out string targetName) ||
            !IsObjectLikeType(targetType) ||
            !TryParseParameter(parameters[1], out string memberType, out string memberName) ||
            !memberType.Equals("string", StringComparison.Ordinal))
        {
            return false;
        }

        string valueName = string.Empty;
        if (kind == DynamicHelperKind.Setter &&
            !TryParseParameter(parameters[2], out _, out valueName))
        {
            return false;
        }

        string indent = line[..(line.Length - line.TrimStart().Length)];
        signature = new DynamicHelperSignature(indent, kind, targetName, memberName, valueName);
        return true;
    }

    private static bool TryParseParameter(string parameter, out string type, out string name)
    {
        type = string.Empty;
        name = string.Empty;
        string[] tokens = parameter.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        type = tokens[^2];
        name = tokens[^1].TrimStart('@');
        return IsIdentifier(name);
    }

    private static bool IsObjectLikeType(string type) =>
        type.Equals("object", StringComparison.Ordinal) ||
        type.Equals("object?", StringComparison.Ordinal) ||
        type.Equals("System.Object", StringComparison.Ordinal) ||
        type.Equals("System.Object?", StringComparison.Ordinal);

    private static bool CanRewriteDynamicHelperBody(
        IReadOnlyList<string> lines,
        int startLine,
        int endLine,
        DynamicHelperSignature signature)
    {
        if (startLine > endLine || startLine < 0 || endLine >= lines.Count)
        {
            return false;
        }

        string body = string.Join('\n', lines.Skip(startLine).Take(endLine - startLine + 1));
        if (!ContainsDynamicNamedLookup(body, "Property", signature.TargetParameterName, signature.MemberParameterName) ||
            !ContainsDynamicNamedLookup(body, "Field", signature.TargetParameterName, signature.MemberParameterName))
        {
            return false;
        }

        return signature.Kind == DynamicHelperKind.Getter
            ? body.Contains(".GetValue(", StringComparison.Ordinal)
            : body.Contains(".SetValue(", StringComparison.Ordinal);
    }

    private static bool ContainsDynamicNamedLookup(string body, string kind, string targetName, string memberName)
    {
        string directLookup = $"{targetName}.GetType().Get{kind}({memberName}";
        if (body.Contains(directLookup, StringComparison.Ordinal))
        {
            return true;
        }

        Match typeAssignment = Regex.Match(
            body,
            $@"\b(?:Type|System\.Type)\s+(?<typeName>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*{Regex.Escape(targetName)}\.GetType\s*\(\s*\)\s*;",
            RegexOptions.Multiline);
        return typeAssignment.Success &&
               body.Contains($"{typeAssignment.Groups["typeName"].Value}.Get{kind}({memberName}", StringComparison.Ordinal);
    }

    private static bool CanRewriteDynamicInstanceFallback(string source, int line)
    {
        if (line <= 0)
        {
            return false;
        }

        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        int lineIndex = line - 1;
        return lineIndex >= 0 &&
               lineIndex < lines.Length &&
               TryFindContainingMethod(lines, lineIndex, out int methodLine, out MethodSignature signature) &&
               DynamicFallbackReturnTypes.Contains(signature.ReturnType) &&
               TryFindMethodBody(lines, methodLine, out int openBraceLine, out int closeBraceLine) &&
               TryFindDynamicInstanceFallback(lines, openBraceLine + 1, closeBraceLine - 1, out _, out _);
    }

    private static bool CanRewriteDynamicHelperMethod(string source, int line)
    {
        if (line <= 0)
        {
            return false;
        }

        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        int lineIndex = line - 1;
        return lineIndex >= 0 &&
               lineIndex < lines.Length &&
               TryFindContainingDynamicHelperMethod(lines, lineIndex, out int methodLine, out DynamicHelperSignature signature) &&
               TryFindMethodBody(lines, methodLine, out int openBraceLine, out int closeBraceLine) &&
               CanRewriteDynamicHelperBody(lines, openBraceLine + 1, closeBraceLine - 1, signature);
    }

    private static bool TryFindContainingDynamicHelperMethod(
        IReadOnlyList<string> lines,
        int memberLine,
        out int methodLine,
        out DynamicHelperSignature signature)
    {
        for (int index = memberLine; index >= 0; index--)
        {
            if (TryParseDynamicHelperSignature(lines[index], out signature))
            {
                methodLine = index;
                return true;
            }
        }

        methodLine = -1;
        signature = default;
        return false;
    }

    private static bool TryFindDynamicInstanceFallback(
        IReadOnlyList<string> lines,
        int startLine,
        int endLine,
        out string parameterName,
        out string memberName)
    {
        parameterName = string.Empty;
        memberName = string.Empty;
        if (startLine > endLine || startLine < 0 || endLine >= lines.Count)
        {
            return false;
        }

        var lookups = new List<(string Receiver, string Member, MemberAccessKind Kind)>();
        for (int index = startLine; index <= endLine; index++)
        {
            foreach ((string receiver, string member, MemberAccessKind kind) in GetDynamicInstanceLookups(lines[index]))
            {
                lookups.Add((receiver, member, kind));
            }
        }

        foreach (var group in lookups.GroupBy(lookup => (lookup.Receiver, lookup.Member)))
        {
            bool hasField = group.Any(lookup => lookup.Kind == MemberAccessKind.Field);
            bool hasProperty = group.Any(lookup => lookup.Kind == MemberAccessKind.Property);
            if (!hasField || !hasProperty)
            {
                continue;
            }

            parameterName = group.Key.Receiver;
            memberName = group.Key.Member;
            return true;
        }

        return false;
    }

    private static IEnumerable<(string Receiver, string Member, MemberAccessKind Kind)> GetDynamicInstanceLookups(string line)
    {
        foreach (Match match in DynamicInstanceLookupPattern.Matches(line))
        {
            yield return (
                match.Groups["receiver"].Value,
                match.Groups["member"].Value,
                match.Groups["kind"].Value.Equals("Field", StringComparison.Ordinal) ? MemberAccessKind.Field : MemberAccessKind.Property);
        }
    }

    private static bool ContainsDynamicInstanceLookup(string line) =>
        line.Contains(".GetType()", StringComparison.Ordinal) &&
        (line.Contains(".GetField(", StringComparison.Ordinal) || line.Contains(".GetProperty(", StringComparison.Ordinal));

    private static int FindTypedMemberAccessLine(IReadOnlyList<string> lines, MemberAccessTarget target)
    {
        string fieldAccess = $"typeof({target.OwnerAlias}).GetField(\"{target.MemberName}\"";
        string propertyAccess = $"typeof({target.OwnerAlias}).GetProperty(\"{target.MemberName}\"";
        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].Contains(fieldAccess, StringComparison.Ordinal) ||
                lines[index].Contains(propertyAccess, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryFindContainingMethod(
        IReadOnlyList<string> lines,
        int memberLine,
        out int methodLine,
        out MethodSignature signature)
    {
        for (int index = memberLine; index >= 0; index--)
        {
            if (TryParseMethodSignature(lines[index], out signature))
            {
                methodLine = index;
                return true;
            }
        }

        methodLine = -1;
        signature = default;
        return false;
    }

    private static bool TryParseMethodSignature(string line, out MethodSignature signature)
    {
        signature = default;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.Contains("=>", StringComparison.Ordinal) ||
            trimmed.Contains("typeof(", StringComparison.Ordinal) ||
            !trimmed.Contains('(', StringComparison.Ordinal) ||
            !trimmed.Contains(')', StringComparison.Ordinal))
        {
            return false;
        }

        string firstToken = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (firstToken is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock" or "return")
        {
            return false;
        }

        int openParen = trimmed.IndexOf('(');
        int closeParen = trimmed.IndexOf(')', openParen + 1);
        if (openParen <= 0 || closeParen <= openParen)
        {
            return false;
        }

        string beforeParen = trimmed[..openParen].Trim();
        string[] beforeTokens = beforeParen.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (beforeTokens.Length < 2 || beforeTokens.Any(token => token.Contains('.', StringComparison.Ordinal)))
        {
            return false;
        }

        string methodName = beforeTokens[^1];
        if (!IsIdentifier(methodName))
        {
            return false;
        }

        string returnType = beforeTokens[^2];
        if (MethodModifiers.Contains(returnType))
        {
            return false;
        }

        string parameters = trimmed[(openParen + 1)..closeParen].Trim();
        if (parameters.Length == 0 || parameters.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        string[] parameterTokens = parameters.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parameterTokens.Length < 2)
        {
            return false;
        }

        string parameterName = parameterTokens[^1].TrimStart('@');
        if (!IsIdentifier(parameterName))
        {
            return false;
        }

        string indent = line[..(line.Length - line.TrimStart().Length)];
        signature = new MethodSignature(indent, returnType, parameterName);
        return true;
    }

    private static bool TryFindMethodBody(IReadOnlyList<string> lines, int methodLine, out int openBraceLine, out int closeBraceLine)
    {
        openBraceLine = -1;
        closeBraceLine = -1;
        for (int index = methodLine; index < lines.Count; index++)
        {
            if (lines[index].Contains('{', StringComparison.Ordinal))
            {
                openBraceLine = index;
                break;
            }

            if (index > methodLine + 3 ||
                (index > methodLine && TryParseMethodSignature(lines[index], out _)))
            {
                return false;
            }
        }

        if (openBraceLine < 0)
        {
            return false;
        }

        int depth = 0;
        bool started = false;
        for (int index = openBraceLine; index < lines.Count; index++)
        {
            foreach (char character in lines[index])
            {
                if (character == '{')
                {
                    depth++;
                    started = true;
                }
                else if (character == '}')
                {
                    depth--;
                    if (started && depth == 0)
                    {
                        closeBraceLine = index;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool CanRewriteBody(
        IReadOnlyList<string> lines,
        int start,
        int end,
        MemberAccessTarget target,
        string parameterName)
    {
        if (start > end)
        {
            return false;
        }

        string body = string.Join('\n', lines.Skip(start).Take(end - start + 1));
        return !body.Contains('#', StringComparison.Ordinal) &&
               body.Contains($"typeof({target.OwnerAlias}).GetField(\"{target.MemberName}\"", StringComparison.Ordinal) &&
               body.Contains($"typeof({target.OwnerAlias}).GetProperty(\"{target.MemberName}\"", StringComparison.Ordinal) &&
               body.Contains($"GetValue({parameterName})", StringComparison.Ordinal);
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private readonly record struct MethodSignature(string Indent, string ReturnType, string ParameterName);

    private enum DynamicHelperKind
    {
        Getter,
        Setter
    }

    private readonly record struct DynamicHelperSignature(
        string Indent,
        DynamicHelperKind Kind,
        string TargetParameterName,
        string MemberParameterName,
        string ValueParameterName);
}
