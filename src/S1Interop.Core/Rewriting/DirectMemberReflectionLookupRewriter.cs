namespace S1Interop.Core;

public sealed class DirectMemberReflectionLookupRewriter
{
    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("DirectMemberReflectionLookup", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(risk.FilePath))
        {
            return false;
        }

        MemberAccessTarget[] targets = new MemberAccessTargetCatalog()
            .DiscoverFileTargets(risk.FilePath)
            .Where(HasExactTypedAccessor)
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
            .Where(HasExactTypedAccessor)
            .OrderByDescending(target => target.Line)
            .ToArray();
        if (sourceTargets.Length == 0 && projectTargets.Count == 1)
        {
            sourceTargets = projectTargets
                .Where(HasExactTypedAccessor)
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

    private static bool TryRewriteLine(List<string> lines, int startIndex, IReadOnlyList<MemberAccessTarget> targets)
    {
        if (startIndex < 0 || startIndex >= lines.Count)
        {
            return false;
        }

        if (!TryFindStatementEnd(lines, startIndex, out int endIndex))
        {
            return false;
        }

        string firstLine = lines[startIndex];
        string statement = string.Join(" ", lines.Skip(startIndex).Take(endIndex - startIndex + 1).Select(line => line.Trim()));
        MemberAccessTarget? target = targets.FirstOrDefault(target => CanRewriteStatement(statement, target));
        if (target is null)
        {
            return false;
        }

        int typeOfIndex = firstLine.IndexOf("typeof", StringComparison.Ordinal);
        if (typeOfIndex < 0)
        {
            return false;
        }

        string prefix = firstLine[..typeOfIndex];
        string replacement = $"{prefix}S1Interop.Generated.S1InteropMemberRegistry.{target.MemberAlias}{GetAccessorSuffix(target)};";
        lines.RemoveRange(startIndex, endIndex - startIndex + 1);
        lines.Insert(startIndex, replacement);
        return true;
    }

    private static bool CanRewriteLine(string source, int line, IReadOnlyList<MemberAccessTarget> targets)
    {
        if (line <= 0 || targets.Count == 0)
        {
            return false;
        }

        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        int startIndex = line - 1;
        if (startIndex >= lines.Length ||
            !TryFindStatementEnd(lines, startIndex, out int endIndex))
        {
            return false;
        }

        string statement = string.Join(" ", lines.Skip(startIndex).Take(endIndex - startIndex + 1).Select(sourceLine => sourceLine.Trim()));
        return targets.Any(target => CanRewriteStatement(statement, target));
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

    private static bool CanRewriteStatement(string statement, MemberAccessTarget target)
    {
        string methodName = target.Kind == MemberAccessKind.Field ? "GetField" : "GetProperty";
        return StartsWithSafeStatementPrefix(statement) &&
               (statement.Contains($"typeof({target.OwnerAlias})", StringComparison.Ordinal) ||
                statement.Contains($"typeof({target.OwnerTypeName})", StringComparison.Ordinal)) &&
               statement.Contains($".{methodName}(\"{target.MemberName}\"", StringComparison.Ordinal) &&
               !statement.Contains("?.", StringComparison.Ordinal);
    }

    private static bool StartsWithSafeStatementPrefix(string statement)
    {
        string trimmed = statement.TrimStart();
        return trimmed.StartsWith("return typeof", StringComparison.Ordinal) ||
               trimmed.StartsWith("var ", StringComparison.Ordinal) ||
               trimmed.StartsWith("FieldInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("FieldInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("PropertyInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("PropertyInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.FieldInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.FieldInfo? ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.PropertyInfo ", StringComparison.Ordinal) ||
               trimmed.StartsWith("System.Reflection.PropertyInfo? ", StringComparison.Ordinal);
    }

    private static bool HasExactTypedAccessor(MemberAccessTarget target) =>
        target.Kind is MemberAccessKind.Field or MemberAccessKind.Property;

    private static string GetAccessorSuffix(MemberAccessTarget target) =>
        target.Kind == MemberAccessKind.Field ? "FieldInfo" : "PropertyInfo";
}
