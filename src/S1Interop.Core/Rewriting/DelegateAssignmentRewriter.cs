using System.Text.RegularExpressions;

namespace S1Interop.Core;

public sealed class DelegateAssignmentRewriter
{
    private static readonly Regex AssignmentRegex = new(
        @"^(?<indent>\s*)(?<target>[^=;{}?:]+?)\s*=\s*(?<cast>\([^)]*\)\s*)?(?:System\.)?Delegate\.(?<method>Combine|Remove)\(\s*(?<current>[^,]+?)\s*,\s*(?<listener>.+)\s*\);(?<suffix>\s*(?://.*)?)$",
        RegexOptions.Compiled);

    private static readonly Regex SourceRiskEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<source>.+)$",
        RegexOptions.Compiled);

    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("DirectDelegateCombine", StringComparison.OrdinalIgnoreCase))
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
            trimmed.Contains("S1InteropDelegateEventBridge.", StringComparison.Ordinal) ||
            trimmed.Contains("Il2CppSystem.Delegate.", StringComparison.Ordinal) ||
            trimmed.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal) ||
            trimmed.Contains("EventHelper.", StringComparison.Ordinal))
        {
            return false;
        }

        Match match = AssignmentRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        string target = match.Groups["target"].Value.Trim();
        string current = match.Groups["current"].Value.Trim();
        string listener = match.Groups["listener"].Value.Trim();
        if (!IsSafeExpression(target) ||
            !IsSafeExpression(current) ||
            !IsSafeListener(listener) ||
            !string.Equals(NormalizeExpression(target), NormalizeExpression(current), StringComparison.Ordinal))
        {
            return false;
        }

        string helperMethod = match.Groups["method"].Value.Equals("Remove", StringComparison.Ordinal)
            ? "Remove"
            : "Combine";
        string cast = match.Groups["cast"].Value;
        rewrittenLine =
            $"{match.Groups["indent"].Value}{target} = {cast}S1Interop.Generated.S1InteropDelegateEventBridge.{helperMethod}({current}, {listener});{match.Groups["suffix"].Value}";
        return true;
    }

    private static string ExtractSourceLine(string evidence)
    {
        Match match = SourceRiskEvidenceRegex.Match(evidence);
        return match.Success ? match.Groups["source"].Value : evidence;
    }

    private static bool IsSafeExpression(string value) =>
        value.Length > 0 &&
        !value.Contains(';', StringComparison.Ordinal) &&
        !value.Contains('{', StringComparison.Ordinal) &&
        !value.Contains('}', StringComparison.Ordinal) &&
        !value.Contains('?', StringComparison.Ordinal) &&
        !value.Contains(':', StringComparison.Ordinal) &&
        !value.Contains("=>", StringComparison.Ordinal);

    private static bool IsSafeListener(string value) =>
        IsSafeExpression(value) &&
        !value.Contains("Delegate.Combine", StringComparison.Ordinal) &&
        !value.Contains("Delegate.Remove", StringComparison.Ordinal);

    private static string NormalizeExpression(string value)
    {
        string normalized = Regex.Replace(value, @"\s+", string.Empty);
        while (normalized.Length > 1 &&
               normalized[0] == '(' &&
               normalized[^1] == ')' &&
               HasSingleOuterParentheses(normalized))
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    private static bool HasSingleOuterParentheses(string value)
    {
        int depth = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')')
            {
                depth--;
                if (depth == 0 && index < value.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }
}
