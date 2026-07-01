namespace S1Interop.Core;

public sealed class HarmonyOverloadBindingRewriter
{
    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("HarmonyOverloadBinding", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(risk.FilePath))
        {
            return false;
        }

        return new HarmonyMethodTargetCatalog()
            .DiscoverFileTargets(risk.FilePath)
            .Any(target => target.StartLine == risk.Line);
    }

    public string RewriteSource(string source, string sourcePath, IReadOnlyList<HarmonyMethodTarget> projectTargets)
    {
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = source.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  source.EndsWith('\n');
        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (hadTrailingNewline && lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        HarmonyMethodTarget[] sourceTargets = projectTargets
            .Where(target => string.Equals(target.SourceFilePath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(target => target.StartLine)
            .ToArray();
        if (sourceTargets.Length == 0)
        {
            return source;
        }

        var rewrittenLines = lines.ToList();
        bool changed = false;
        foreach (HarmonyMethodTarget target in sourceTargets)
        {
            int startIndex = target.StartLine - 1;
            int endIndex = target.EndLine - 1;
            if (startIndex < 0 || endIndex >= rewrittenLines.Count || startIndex > endIndex)
            {
                continue;
            }

            string startLine = rewrittenLines[startIndex];
            int accessToolsIndex = startLine.IndexOf("AccessTools.Method", StringComparison.Ordinal);
            if (accessToolsIndex < 0 || !startLine.Contains(target.VariableName, StringComparison.Ordinal))
            {
                continue;
            }

            string prefix = startLine[..accessToolsIndex];
            string replacement = $"{prefix}S1Interop.Generated.S1InteropMemberRegistry.{target.MethodAlias}Method;";
            rewrittenLines.RemoveRange(startIndex, endIndex - startIndex + 1);
            rewrittenLines.Insert(startIndex, replacement);
            changed = true;
        }

        if (!changed)
        {
            return source;
        }

        string rewritten = string.Join(newline, rewrittenLines);
        return hadTrailingNewline ? rewritten + newline : rewritten;
    }
}
