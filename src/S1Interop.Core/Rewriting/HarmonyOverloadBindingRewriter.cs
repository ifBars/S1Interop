namespace S1Interop.Core.Rewriting;

/// <summary>
/// Replaces supported Harmony overload lookups with generated method metadata bindings.
/// </summary>
public sealed class HarmonyOverloadBindingRewriter
{
    /// <summary>
    /// Checks whether a source risk matches a discovered Harmony method target.
    /// </summary>
    /// <param name="risk">The source risk to inspect.</param>
    /// <returns>True when a target starts on the recorded source line; otherwise, false.</returns>
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

    /// <summary>
    /// Rewrites supported Harmony method lookups in one source file.
    /// </summary>
    /// <param name="source">The original C# source.</param>
    /// <param name="sourcePath">The path used to match source locations in <paramref name="projectTargets"/>.</param>
    /// <param name="projectTargets">The discovered Harmony targets for the project.</param>
    /// <returns>The rewritten source, or the original source when no matching lookup can be changed safely.</returns>
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
            int rewriteIndex = startLine.IndexOf("AccessTools.Method", StringComparison.Ordinal);
            if (rewriteIndex < 0)
            {
                rewriteIndex = startLine.IndexOf("typeof", StringComparison.Ordinal);
            }

            if (rewriteIndex < 0 || !startLine.Contains(target.VariableName, StringComparison.Ordinal))
            {
                continue;
            }

            string prefix = startLine[..rewriteIndex];
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
