using System.Text.RegularExpressions;

namespace S1Interop.Core.Rewriting;

public sealed class Il2CppObjectCastRewriter
{
    private static readonly Regex PatternMatchRegex = new(
        @"^(?<indent>\s*)if\s*\(\s*(?<value>.+?)\s+is\s+(?<type>[A-Za-z_][A-Za-z0-9_.<>?]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\)(?<suffix>\s*(?://.*)?)$",
        RegexOptions.Compiled);

    private static readonly Regex ReturnTernaryPatternMatchRegex = new(
        @"^(?<indent>\s*)return\s+(?<value>.+?)\s+is\s+(?<type>[A-Za-z_][A-Za-z0-9_.<>?]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\?\s*(?<whenTrue>.+?)\s*:\s*(?<whenFalse>.+?)\s*;(?<suffix>\s*(?://.*)?)$",
        RegexOptions.Compiled);

    private static readonly Regex SourceRiskEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<source>.+)$",
        RegexOptions.Compiled);

    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("Il2CppObjectCastInterop", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryRewriteLine(ExtractSourceLine(risk.Evidence), out _);
    }

    public string RewriteSource(string source)
    {
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = source.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  source.EndsWith('\n');
        string[] lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (hadTrailingNewline && lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        bool changed = false;
        for (int index = 0; index < lines.Length; index++)
        {
            if (!TryRewriteLine(lines[index], out string rewrittenLine))
            {
                continue;
            }

            lines[index] = rewrittenLine;
            changed = true;
        }

        if (!changed)
        {
            return source;
        }

        string rewritten = string.Join(newline, lines);
        return hadTrailingNewline ? rewritten + newline : rewritten;
    }

    private static bool TryRewriteLine(string line, out string rewrittenLine)
    {
        rewrittenLine = string.Empty;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.Contains("S1InteropObjectCast.", StringComparison.Ordinal) ||
            trimmed.Contains("Il2CppObjectBase", StringComparison.Ordinal) ||
            trimmed.Contains("TryCast<", StringComparison.Ordinal))
        {
            return false;
        }

        Match match = PatternMatchRegex.Match(line);
        if (match.Success)
        {
            return TryRewriteIfPattern(match, out rewrittenLine);
        }

        match = ReturnTernaryPatternMatchRegex.Match(line);
        return match.Success && TryRewriteReturnTernaryPattern(match, out rewrittenLine);
    }

    private static bool TryRewriteIfPattern(Match match, out string rewrittenLine)
    {
        rewrittenLine = string.Empty;
        string value = match.Groups["value"].Value.Trim();
        string type = match.Groups["type"].Value.TrimEnd('?');
        string name = match.Groups["name"].Value;
        if (!IsSafeValueExpression(value) || !IsSafeTypeName(type))
        {
            return false;
        }

        rewrittenLine = $"{match.Groups["indent"].Value}if (S1Interop.Generated.S1InteropObjectCast.Is<{type}>({value}, out {type}? {name})){match.Groups["suffix"].Value}";
        return true;
    }

    private static bool TryRewriteReturnTernaryPattern(Match match, out string rewrittenLine)
    {
        rewrittenLine = string.Empty;
        string value = match.Groups["value"].Value.Trim();
        string type = match.Groups["type"].Value.TrimEnd('?');
        string name = match.Groups["name"].Value;
        string whenTrue = match.Groups["whenTrue"].Value.Trim();
        string whenFalse = match.Groups["whenFalse"].Value.Trim();
        if (!IsSafeValueExpression(value) ||
            !IsSafeTypeName(type) ||
            !IsSafeTernaryArm(whenTrue) ||
            !IsSafeTernaryArm(whenFalse))
        {
            return false;
        }

        rewrittenLine = $"{match.Groups["indent"].Value}return S1Interop.Generated.S1InteropObjectCast.Is<{type}>({value}, out {type}? {name}) ? {whenTrue} : {whenFalse};{match.Groups["suffix"].Value}";
        return true;
    }

    private static string ExtractSourceLine(string evidence)
    {
        Match match = SourceRiskEvidenceRegex.Match(evidence);
        return match.Success ? match.Groups["source"].Value : evidence;
    }

    private static bool IsSafeValueExpression(string value) =>
        value.Length > 0 &&
        !value.Contains(';', StringComparison.Ordinal) &&
        !value.Contains('{', StringComparison.Ordinal) &&
        !value.Contains('}', StringComparison.Ordinal) &&
        !value.Contains("=>", StringComparison.Ordinal);

    private static bool IsSafeTypeName(string value) =>
        value.Length > 0 &&
        !value.Contains('&', StringComparison.Ordinal) &&
        !value.Contains('*', StringComparison.Ordinal) &&
        IsObjectBackedTypeName(value);

    private static bool IsObjectBackedTypeName(string value) =>
        value.Contains("UniversalRenderPipelineAsset", StringComparison.Ordinal) ||
        value.Contains("UnityEngine.Object", StringComparison.Ordinal) ||
        value.Contains("Component", StringComparison.Ordinal) ||
        value.Contains("MonoBehaviour", StringComparison.Ordinal);

    private static bool IsSafeTernaryArm(string value) =>
        value.Length > 0 &&
        !value.Contains(';', StringComparison.Ordinal) &&
        !value.Contains('{', StringComparison.Ordinal) &&
        !value.Contains('}', StringComparison.Ordinal) &&
        !value.Contains("=>", StringComparison.Ordinal);
}
