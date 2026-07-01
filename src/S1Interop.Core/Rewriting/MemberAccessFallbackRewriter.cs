namespace S1Interop.Core;

public sealed class MemberAccessFallbackRewriter
{
    private static readonly HashSet<string> UnsupportedReturnTypes = new(StringComparer.Ordinal)
    {
        "bool",
        "byte",
        "char",
        "decimal",
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
        if (target is null)
        {
            return false;
        }

        string source = File.ReadAllText(risk.FilePath);
        return new MemberAccessFallbackRewriter().RewriteSource(source, risk.FilePath, [target]) != source;
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

        if (sourceTargets.Length == 0)
        {
            return source;
        }

        var rewrittenLines = lines.ToList();
        bool changed = false;
        foreach (MemberAccessTarget target in sourceTargets)
        {
            if (!TryRewriteTarget(rewrittenLines, target))
            {
                continue;
            }

            changed = true;
        }

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

        string castType = signature.ReturnType.EndsWith("?", StringComparison.Ordinal)
            ? signature.ReturnType[..^1]
            : signature.ReturnType;
        if (UnsupportedReturnTypes.Contains(castType))
        {
            return false;
        }

        string replacement = $"{signature.Indent}    return S1Interop.Generated.S1InteropMemberRegistry.Get{target.MemberAlias}<{castType}>({signature.ParameterName});";
        lines.RemoveRange(openBraceLine + 1, closeBraceLine - openBraceLine - 1);
        lines.Insert(openBraceLine + 1, replacement);
        return true;
    }

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
}
