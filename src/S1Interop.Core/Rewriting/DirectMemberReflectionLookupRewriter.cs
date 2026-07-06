using System.Text.RegularExpressions;

namespace S1Interop.Core.Rewriting;

public sealed class DirectMemberReflectionLookupRewriter
{
    private static readonly Regex ReflectionLookupReceiverRegex = new(
        @"(?<receiver>typeof\s*\(\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)|[A-Za-z_][A-Za-z0-9_.]*\s*\.\s*GetType\s*\(\s*\))\s*\.\s*Get(?<kind>Field|Property)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex AccessToolsFieldOrPropertyRegex = new(
        @"AccessTools\.(?<kind>Field|Property(?:Getter|Setter)?)\s*\(\s*typeof\s*\(\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\)\s*,\s*""(?<member>[A-Za-z_][A-Za-z0-9_]*)""",
        RegexOptions.Compiled);

    private static readonly Regex AccessToolsPropertyAccessorRegex = new(
        @"AccessTools\.Property(?<accessor>Getter|Setter)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex PropertyAccessorRegex = new(
        @"\.GetProperty\s*\([^)]*\)\s*\.\s*(?<accessor>GetMethod|SetMethod)\b",
        RegexOptions.Compiled);

    public static bool CanRewrite(SourceRisk risk)
    {
        if (!IsSupportedRiskKind(risk) || !File.Exists(risk.FilePath))
        {
            return false;
        }

        MemberAccessTarget[] targets = new MemberAccessTargetCatalog()
            .DiscoverFileTargets(risk.FilePath)
            .Where(HasTypedAccessor)
            .ToArray();
        string source = File.ReadAllText(risk.FilePath);
        return CanRewriteLine(source, risk.Line, targets);
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
            .Where(HasTypedAccessor)
            .OrderByDescending(target => target.Line)
            .ToArray();
        if (sourceTargets.Length == 0 && projectTargets.Count == 1)
        {
            sourceTargets = projectTargets
                .Where(HasTypedAccessor)
                .OrderByDescending(target => target.Line)
                .ToArray();
        }

        if (sourceTargets.Length == 0)
        {
            return source;
        }

        var rewrittenLines = lines.ToList();
        bool changed = false;
        for (int index = rewrittenLines.Count - 1; index >= 0; index--)
        {
            if (TryRewriteLine(rewrittenLines, index, sourceTargets))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return source;
        }

        string rewritten = string.Join(newline, rewrittenLines);
        return hadTrailingNewline ? rewritten + newline : rewritten;
    }

    private static bool TryRewriteLine(List<string> lines, int lineIndex, IReadOnlyList<MemberAccessTarget> targets)
    {
        if (!TryFindStatementStart(lines, lineIndex, out int startIndex))
        {
            return false;
        }

        if (!TryFindStatementEnd(lines, startIndex, out int endIndex))
        {
            return false;
        }

        string firstLine = lines[startIndex];
        string statement = string.Join(" ", lines.Skip(startIndex).Take(endIndex - startIndex + 1).Select(line => line.Trim()));
        if (IsPartOfFieldPropertyFallbackPair(lines, startIndex, statement))
        {
            return false;
        }

        MemberAccessTarget? target = null;
        MemberAccessKind lookupKind = MemberAccessKind.FieldOrProperty;
        foreach (MemberAccessTarget candidate in targets)
        {
            if (!CanUseTargetForStatement(candidate, startIndex, endIndex))
            {
                continue;
            }

            if (CanRewriteStatement(statement, candidate, out lookupKind))
            {
                target = candidate;
                break;
            }
        }

        if (target is null)
        {
            return false;
        }

        if (!TryGetReplacementPrefix(firstLine, out string prefix))
        {
            return false;
        }

        string replacement = $"{prefix}{GetReplacementExpression(target, lookupKind, statement)};";
        lines.RemoveRange(startIndex, endIndex - startIndex + 1);
        lines.Insert(startIndex, replacement);
        return true;
    }

    private static bool CanUseTargetForStatement(MemberAccessTarget target, int startIndex, int endIndex)
    {
        int targetIndex = target.Line - 1;
        return targetIndex >= startIndex && targetIndex <= endIndex;
    }

    private static bool IsPartOfFieldPropertyFallbackPair(IReadOnlyList<string> lines, int startIndex, string statement)
    {
        Match lookup = ReflectionLookupReceiverRegex.Match(statement);
        if (!lookup.Success)
        {
            return false;
        }

        string receiver = NormalizeReceiver(lookup.Groups["receiver"].Value);
        string oppositeKind = lookup.Groups["kind"].Value.Equals("Field", StringComparison.Ordinal)
            ? "Property"
            : "Field";
        string sourceWindow = GetSourceWindow(lines, Math.Max(0, startIndex - 8), maxLineCount: 20);
        foreach (Match candidate in ReflectionLookupReceiverRegex.Matches(sourceWindow))
        {
            if (candidate.Groups["kind"].Value.Equals(oppositeKind, StringComparison.Ordinal) &&
                NormalizeReceiver(candidate.Groups["receiver"].Value).Equals(receiver, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSourceWindow(IReadOnlyList<string> lines, int startIndex, int maxLineCount)
    {
        int endIndex = Math.Min(lines.Count, startIndex + maxLineCount);
        return string.Join('\n', lines.Skip(startIndex).Take(endIndex - startIndex));
    }

    private static string NormalizeReceiver(string receiver) =>
        Regex.Replace(receiver, @"\s+", string.Empty);

    private static bool TryGetReplacementPrefix(string firstLine, out string prefix)
    {
        int accessToolsIndex = firstLine.IndexOf("AccessTools.", StringComparison.Ordinal);
        if (accessToolsIndex >= 0)
        {
            prefix = firstLine[..accessToolsIndex];
            return true;
        }

        int typeOfIndex = firstLine.IndexOf("typeof", StringComparison.Ordinal);
        if (typeOfIndex >= 0)
        {
            prefix = firstLine[..typeOfIndex];
            return true;
        }

        int equalsIndex = firstLine.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex > 0)
        {
            prefix = firstLine[..(equalsIndex + 1)] + " ";
            return true;
        }

        prefix = string.Empty;
        return false;
    }

    private static bool CanRewriteLine(string source, int line, IReadOnlyList<MemberAccessTarget> targets)
    {
        if (line <= 0 || targets.Count == 0)
        {
            return false;
        }

        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (!TryFindStatementStart(lines, line - 1, out int startIndex))
        {
            return false;
        }

        if (startIndex >= lines.Length ||
            !TryFindStatementEnd(lines, startIndex, out int endIndex))
        {
            return false;
        }

        string statement = string.Join(" ", lines.Skip(startIndex).Take(endIndex - startIndex + 1).Select(sourceLine => sourceLine.Trim()));
        return targets.Any(target => CanUseTargetForStatement(target, startIndex, endIndex) && CanRewriteStatement(statement, target, out _));
    }

    private static bool TryFindStatementStart(IReadOnlyList<string> lines, int lineIndex, out int startIndex)
    {
        startIndex = -1;
        if (lineIndex < 0 || lineIndex >= lines.Count)
        {
            return false;
        }

        if (CanStartStatement(lines[lineIndex]))
        {
            startIndex = lineIndex;
            return true;
        }

        if (!lines[lineIndex].Contains("typeof", StringComparison.Ordinal))
        {
            return false;
        }

        int minIndex = Math.Max(0, lineIndex - 3);
        for (int index = lineIndex - 1; index >= minIndex; index--)
        {
            if (lines[index].Contains(';', StringComparison.Ordinal))
            {
                return false;
            }

            if (CanStartStatement(lines[index]))
            {
                startIndex = index;
                return true;
            }
        }

        return false;
    }

    private static bool CanStartStatement(string line)
    {
        string trimmed = line.TrimStart();
        return StartsWithSafeStatementPrefix(trimmed) ||
               IsAssignmentPrefix(trimmed);
    }

    private static bool TryFindStatementEnd(IReadOnlyList<string> lines, int startIndex, out int endIndex)
    {
        endIndex = -1;
        int maxEnd = Math.Min(lines.Count - 1, startIndex + 5);
        for (int index = startIndex; index <= maxEnd; index++)
        {
            if (lines[index].Contains(';', StringComparison.Ordinal))
            {
                endIndex = index;
                return true;
            }
        }

        return false;
    }

    private static bool CanRewriteStatement(string statement, MemberAccessTarget target, out MemberAccessKind lookupKind)
    {
        lookupKind = MemberAccessKind.FieldOrProperty;
        if (!(StartsWithSafeStatementPrefix(statement) || IsAssignmentPrefix(statement)) ||
            !HasSupportedReceiver(statement, target) ||
            !TryGetLookupKind(statement, target.MemberName, out lookupKind) ||
            statement.Contains("?.", StringComparison.Ordinal))
        {
            return false;
        }

        return target.Kind == MemberAccessKind.FieldOrProperty || target.Kind == lookupKind;
    }

    private static bool HasSupportedReceiver(string statement, MemberAccessTarget target) =>
        statement.Contains($"typeof({target.OwnerAlias})", StringComparison.Ordinal) ||
        statement.Contains($"typeof({target.OwnerTypeName})", StringComparison.Ordinal) ||
        statement.Contains("AccessTools.Field", StringComparison.Ordinal) ||
        statement.Contains("AccessTools.Property", StringComparison.Ordinal) ||
        statement.Contains(".GetType().Get", StringComparison.Ordinal) ||
        statement.Contains(".GetType() .Get", StringComparison.Ordinal);

    private static bool StartsWithSafeStatementPrefix(string statement)
    {
        string trimmed = statement.TrimStart();
        return trimmed.StartsWith("return typeof", StringComparison.Ordinal) ||
               trimmed.StartsWith("var ", StringComparison.Ordinal) ||
               trimmed.StartsWith("FieldInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("FieldInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("PropertyInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("PropertyInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("MethodInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("MethodInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.FieldInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.FieldInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.PropertyInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.PropertyInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.MethodInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.MethodInfo? ", StringComparison.Ordinal);
    }

    private static bool IsAssignmentPrefix(string statement)
    {
        int equalsIndex = statement.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex <= 0 || statement[..equalsIndex].Contains("==", StringComparison.Ordinal))
        {
            return false;
        }

        string left = statement[..equalsIndex].Trim();
        return IsIdentifierOrMemberAccess(left);
    }

    private static bool IsIdentifierOrMemberAccess(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        string[] parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(IsIdentifier);
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

    private static bool IsSupportedRiskKind(SourceRisk risk) =>
        risk.Kind.Equals("DirectMemberReflectionLookup", StringComparison.OrdinalIgnoreCase) ||
        risk.Kind.Equals("FieldPropertyReflectionFallback", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetLookupKind(string statement, string memberName, out MemberAccessKind kind)
    {
        Match accessToolsMatch = AccessToolsFieldOrPropertyRegex.Match(statement);
        if (accessToolsMatch.Success &&
            accessToolsMatch.Groups["member"].Value.Equals(memberName, StringComparison.Ordinal))
        {
            kind = accessToolsMatch.Groups["kind"].Value.Equals("Field", StringComparison.Ordinal)
                ? MemberAccessKind.Field
                : MemberAccessKind.Property;
            return true;
        }

        if (statement.Contains($".GetField(\"{memberName}\"", StringComparison.Ordinal))
        {
            kind = MemberAccessKind.Field;
            return true;
        }

        if (statement.Contains($".GetProperty(\"{memberName}\"", StringComparison.Ordinal))
        {
            kind = MemberAccessKind.Property;
            return true;
        }

        kind = MemberAccessKind.FieldOrProperty;
        return false;
    }

    private static bool HasTypedAccessor(MemberAccessTarget target) =>
        target.Kind is MemberAccessKind.Field or MemberAccessKind.Property or MemberAccessKind.FieldOrProperty;

    private static string GetReplacementExpression(MemberAccessTarget target, MemberAccessKind kind, string statement)
    {
        string expression = $"S1Interop.Generated.S1InteropMemberRegistry.{target.MemberAlias}{GetAccessorSuffix(kind)}";
        if (kind != MemberAccessKind.Property)
        {
            return expression;
        }

        Match accessToolsAccessor = AccessToolsPropertyAccessorRegex.Match(statement);
        if (accessToolsAccessor.Success)
        {
            string accessorMember = accessToolsAccessor.Groups["accessor"].Value.Equals("Getter", StringComparison.Ordinal)
                ? "GetMethod"
                : "SetMethod";
            return $"{expression}!.{accessorMember}";
        }

        Match accessor = PropertyAccessorRegex.Match(statement);
        return accessor.Success
            ? $"{expression}!.{accessor.Groups["accessor"].Value}"
            : expression;
    }

    private static string GetAccessorSuffix(MemberAccessKind kind) =>
        kind == MemberAccessKind.Field ? "FieldInfo" : "PropertyInfo";
}
