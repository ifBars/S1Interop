using System.Text.RegularExpressions;

namespace S1Interop.Core;

public sealed class DelegateArgumentRewriter
{
    private static readonly Regex GuiWindowRegex = new(
        @"^(?<indent>\s*)(?<prefix>.*\bGUI\.Window\s*\(\s*(?<id>[^,]+)\s*,\s*(?<rect>[^,]+)\s*,\s*)(?<listener>[A-Za-z_][A-Za-z0-9_\.]*)(?<suffix>\s*,.*)$",
        RegexOptions.Compiled);

    private static readonly Regex SourceRiskEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<source>.+)$",
        RegexOptions.Compiled);

    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("DirectDelegateArgumentInterop", StringComparison.OrdinalIgnoreCase))
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
            trimmed.Contains("S1InteropDelegateBridge.", StringComparison.Ordinal) ||
            trimmed.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal) ||
            trimmed.Contains("new GUI.WindowFunction", StringComparison.Ordinal))
        {
            return false;
        }

        Match match = GuiWindowRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        string listener = match.Groups["listener"].Value.Trim();
        if (!IsSafeListener(listener))
        {
            return false;
        }

        rewrittenLine = $"{match.Groups["indent"].Value}{match.Groups["prefix"].Value}S1Interop.Generated.S1InteropDelegateBridge.Convert<GUI.WindowFunction>({listener}){match.Groups["suffix"].Value}";
        return true;
    }

    private static string ExtractSourceLine(string evidence)
    {
        Match match = SourceRiskEvidenceRegex.Match(evidence);
        return match.Success ? match.Groups["source"].Value : evidence;
    }

    private static bool IsSafeListener(string value) =>
        value.Length > 0 &&
        !value.Contains(';', StringComparison.Ordinal) &&
        !value.Contains('{', StringComparison.Ordinal) &&
        !value.Contains('}', StringComparison.Ordinal) &&
        !value.Contains('?', StringComparison.Ordinal) &&
        !value.Contains(':', StringComparison.Ordinal) &&
        !value.Contains("=>", StringComparison.Ordinal) &&
        !value.Contains('(', StringComparison.Ordinal) &&
        !value.Contains(')', StringComparison.Ordinal);
}
