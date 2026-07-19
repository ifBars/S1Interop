using System.Text.RegularExpressions;

namespace S1Interop.Core.Rewriting;

/// <summary>
/// Rewrites supported UnityEvent listener calls through the generated backend-neutral listener bridge.
/// </summary>
public sealed class UnityEventListenerRewriter
{
    private static readonly Regex ListenerCallRegex = new(
        @"^(?<indent>\s*)(?<target>.+)\.(?<method>AddListener|RemoveListener)\((?<listener>.*)\);(?<suffix>\s*(?://.*)?)$",
        RegexOptions.Compiled);

    private static readonly Regex ListenerStartRegex = new(
        @"^(?<indent>\s*)(?<target>.+)\.(?<method>AddListener|RemoveListener)\((?<listener>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex SourceRiskEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<source>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex SystemActionIdentifierRegex = new(
        @"(?:^|[^\w.])(?:System\.)?Action(?:<[^>]+>)?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex LocalWrappedUnityActionDeclarationRegex = new(
        @"^\s*(?:var|(?:UnityEngine\.Events\.)?UnityAction(?:<[^>]+>)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?:UnityEngine\.Events\.)?UnityAction(?:<[^>]+>)?\s*\(\s*(?<listener>[A-Za-z_][A-Za-z0-9_]*)\s*\)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex WrappedUnityActionAssignmentRegex = new(
        @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?:UnityEngine\.Events\.)?UnityAction(?:<[^>]+>)?\s*\(\s*(?<listener>[A-Za-z_][A-Za-z0-9_]*)\s*\)\s*;",
        RegexOptions.Compiled);

    /// <summary>
    /// Checks whether a source risk contains a supported UnityEvent listener call.
    /// </summary>
    /// <param name="risk">The source risk to inspect.</param>
    /// <returns>True when the recorded call can be rewritten safely; otherwise, false.</returns>
    public static bool CanRewrite(SourceRisk risk)
    {
        if (!risk.Kind.Equals("DirectUnityEventListener", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        HashSet<string> systemActionNames = File.Exists(risk.FilePath)
            ? DiscoverSystemActionListenerNames(File.ReadAllLines(risk.FilePath))
            : new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> wrappedUnityActionNames = File.Exists(risk.FilePath)
            ? DiscoverWrappedUnityActionListenerNames(File.ReadAllLines(risk.FilePath))
            : new Dictionary<string, string>(StringComparer.Ordinal);
        return TryRewriteLine(ExtractSourceLine(risk.Evidence), systemActionNames, wrappedUnityActionNames, out _);
    }

    /// <summary>
    /// Rewrites supported listener calls and removes local wrapper declarations that become unused.
    /// </summary>
    /// <param name="source">The original C# source.</param>
    /// <returns>The rewritten source, or the original source when no supported pattern is found.</returns>
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

        HashSet<string> systemActionNames = DiscoverSystemActionListenerNames(lines);
        IReadOnlyDictionary<string, string> wrappedUnityActionNames = DiscoverWrappedUnityActionListenerNames(lines);
        bool changed = false;
        for (int index = 0; index < lines.Length; index++)
        {
            if (!TryRewriteLine(lines[index], systemActionNames, wrappedUnityActionNames, out string rewrittenLine))
            {
                continue;
            }

            lines[index] = rewrittenLine;
            changed = true;
        }

        if (RemoveDeadWrappedUnityActionDeclarations(lines, wrappedUnityActionNames))
        {
            changed = true;
        }

        if (!changed)
        {
            return source;
        }

        string rewritten = string.Join(newline, lines);
        return hadTrailingNewline ? rewritten + newline : rewritten;
    }

    private static bool RemoveDeadWrappedUnityActionDeclarations(
        string[] lines,
        IReadOnlyDictionary<string, string> wrappedUnityActionNames)
    {
        if (wrappedUnityActionNames.Count == 0)
        {
            return false;
        }

        bool changed = false;
        for (int index = 0; index < lines.Length; index++)
        {
            Match match = LocalWrappedUnityActionDeclarationRegex.Match(lines[index]);
            if (!match.Success)
            {
                match = WrappedUnityActionAssignmentRegex.Match(lines[index]);
            }

            if (!match.Success)
            {
                continue;
            }

            string wrapperName = match.Groups["name"].Value;
            if (!wrappedUnityActionNames.ContainsKey(wrapperName) ||
                IsIdentifierReferencedAfterLine(lines, wrapperName, index))
            {
                continue;
            }

            lines[index] = string.Empty;
            changed = true;
        }

        return changed;
    }

    private static bool IsIdentifierReferencedAfterLine(string[] lines, string identifier, int declarationLineIndex)
    {
        string pattern = $@"\b{Regex.Escape(identifier)}\b";
        for (int index = declarationLineIndex + 1; index < lines.Length; index++)
        {
            if (Regex.IsMatch(lines[index], pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryRewriteLine(
        string line,
        IReadOnlySet<string> systemActionNames,
        IReadOnlyDictionary<string, string> wrappedUnityActionNames,
        out string rewrittenLine)
    {
        rewrittenLine = string.Empty;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.Contains("S1InteropUnityEventBridge.", StringComparison.Ordinal) ||
            IsAlreadyRuntimeSpecificListener(trimmed))
        {
            return false;
        }

        Match match = ListenerCallRegex.Match(line);
        bool completeCall = match.Success;
        if (!completeCall)
        {
            match = ListenerStartRegex.Match(line);
            if (!match.Success)
            {
                return false;
            }
        }

        string target = match.Groups["target"].Value.Trim();
        string listener = match.Groups["listener"].Value.Trim();
        if (!IsSafeTargetExpression(target) ||
            !IsSafeListenerExpression(target, listener, systemActionNames, wrappedUnityActionNames, out string rewrittenListener))
        {
            return false;
        }

        string helperMethod = match.Groups["method"].Value.Equals("RemoveListener", StringComparison.Ordinal)
            ? "Remove"
            : "Add";
        string closeCall = completeCall ? ")" : string.Empty;
        string suffix = completeCall ? $";{match.Groups["suffix"].Value}" : string.Empty;
        rewrittenLine =
            $"{match.Groups["indent"].Value}S1Interop.Generated.S1InteropUnityEventBridge.{helperMethod}({target}, {rewrittenListener}{closeCall}{suffix}";
        return true;
    }

    private static string ExtractSourceLine(string evidence)
    {
        Match match = SourceRiskEvidenceRegex.Match(evidence);
        return match.Success ? match.Groups["source"].Value : evidence;
    }

    private static bool IsAlreadyRuntimeSpecificListener(string line) =>
        line.Contains("new UnityAction", StringComparison.Ordinal) ||
        line.Contains("new UnityEngine.Events.UnityAction", StringComparison.Ordinal) ||
        line.Contains("(UnityAction", StringComparison.Ordinal) ||
        line.Contains("(UnityEngine.Events.UnityAction", StringComparison.Ordinal) ||
        line.Contains("new System.Action", StringComparison.Ordinal) ||
        line.Contains("(System.Action", StringComparison.Ordinal) ||
        line.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal) ||
        line.Contains("EventHelper.AddListener", StringComparison.Ordinal) ||
        line.Contains("EventHelper.RemoveListener", StringComparison.Ordinal) ||
        line.Contains("ButtonUtils.AddListener", StringComparison.Ordinal) ||
        line.Contains("ButtonUtils.RemoveListener", StringComparison.Ordinal) ||
        line.Contains("ToggleUtils.AddListener", StringComparison.Ordinal) ||
        line.Contains("ToggleUtils.RemoveListener", StringComparison.Ordinal);

    private static bool IsSafeTargetExpression(string target)
    {
        string trimmed = target.TrimStart();
        return !trimmed.StartsWith("if ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("if(", StringComparison.Ordinal) &&
               !trimmed.StartsWith("return ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("while ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("for ", StringComparison.Ordinal) &&
               !target.Contains(';', StringComparison.Ordinal) &&
               !target.Contains('{', StringComparison.Ordinal) &&
               !target.Contains('}', StringComparison.Ordinal);
    }

    private static bool IsSafeListenerExpression(
        string target,
        string listener,
        IReadOnlySet<string> systemActionNames,
        IReadOnlyDictionary<string, string> wrappedUnityActionNames,
        out string rewrittenListener)
    {
        rewrittenListener = listener;
        if (listener.StartsWith("new Action", StringComparison.Ordinal))
        {
            return true;
        }

        if (listener.Contains("=>", StringComparison.Ordinal) ||
            listener.StartsWith("delegate", StringComparison.Ordinal))
        {
            return true;
        }

        if (IsIdentifier(listener) && systemActionNames.Contains(listener))
        {
            return true;
        }

        if (IsIdentifier(listener) &&
            wrappedUnityActionNames.TryGetValue(listener, out string? wrappedListener) &&
            wrappedListener is not null)
        {
            rewrittenListener = wrappedListener;
            return true;
        }

        if (target.EndsWith(".onClick", StringComparison.Ordinal) &&
            IsPascalCaseIdentifier(listener))
        {
            rewrittenListener = $"new System.Action({listener})";
            return true;
        }

        return false;
    }

    private static bool IsPascalCaseIdentifier(string value) =>
        value.Length > 0 &&
        char.IsUpper(value[0]) &&
        value.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsIdentifier(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static HashSet<string> DiscoverSystemActionListenerNames(IEnumerable<string> lines)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            foreach (Match match in SystemActionIdentifierRegex.Matches(line))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names;
    }

    private static IReadOnlyDictionary<string, string> DiscoverWrappedUnityActionListenerNames(IEnumerable<string> lines)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match match = LocalWrappedUnityActionDeclarationRegex.Match(line);
            if (!match.Success)
            {
                match = WrappedUnityActionAssignmentRegex.Match(line);
            }

            if (!match.Success)
            {
                continue;
            }

            string name = match.Groups["name"].Value;
            string listener = match.Groups["listener"].Value;
            if (string.Equals(name, listener, StringComparison.Ordinal))
            {
                continue;
            }

            names[name] = listener;
        }

        return names;
    }
}
