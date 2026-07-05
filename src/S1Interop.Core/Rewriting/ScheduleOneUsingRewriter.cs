using System.Text;
using System.Text.RegularExpressions;

namespace S1Interop.Core.Rewriting;

public static class ScheduleOneUsingRewriter
{
    public enum RewriteMode
    {
        ConditionalizeAll,
        PreferGlobalFacade
    }

    private static readonly Regex NormalOrStaticUsingRegex = new(
        @"^(?<indent>\s*)using\s+(?<static>static\s+)?(?<target>ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;\s*$",
        RegexOptions.Compiled);

    private static readonly Regex AliasUsingRegex = new(
        @"^(?<indent>\s*)using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<target>ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> FindFilesWithUnconditionalScheduleOneUsings(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return Array.Empty<string>();
        }

        return WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => !IsGeneratedOrBuildOutput(projectDirectory, file))
            .Where(HasUnconditionalScheduleOneUsing)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool RewriteFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        string original = File.ReadAllText(filePath);
        string rewritten = RewriteSource(original);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        File.WriteAllText(filePath, rewritten, Encoding.UTF8);
        return true;
    }

    public static string RewriteSource(string source)
    {
        return RewriteSource(source, RewriteMode.ConditionalizeAll);
    }

    public static string RewriteSource(string source, RewriteMode mode)
    {
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>(lines.Length);
        int preprocessorDepth = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.TrimStart();
            if (preprocessorDepth == 0 &&
                TryParseUsing(line, out ScheduleOneUsing? parsedUsing) &&
                parsedUsing is not null)
            {
                var usingBlock = new List<ScheduleOneUsing> { parsedUsing };
                int nextIndex = index + 1;
                while (nextIndex < lines.Length &&
                       TryParseUsing(lines[nextIndex], out ScheduleOneUsing? nextUsing) &&
                       nextUsing is not null &&
                       string.Equals(nextUsing.Indent, parsedUsing.Indent, StringComparison.Ordinal))
                {
                    usingBlock.Add(nextUsing);
                    nextIndex++;
                }

                output.AddRange(BuildReplacementBlock(parsedUsing.Indent, usingBlock, mode));
                index = nextIndex - 1;
            }
            else
            {
                output.Add(line);
            }

            preprocessorDepth = UpdatePreprocessorDepth(preprocessorDepth, trimmed);
        }

        return string.Join(newline, output);
    }

    private static bool HasUnconditionalScheduleOneUsing(string filePath)
    {
        int preprocessorDepth = 0;
        foreach (string line in File.ReadLines(filePath))
        {
            string trimmed = line.TrimStart();
            if (preprocessorDepth == 0 && TryParseUsing(line, out _))
            {
                return true;
            }

            preprocessorDepth = UpdatePreprocessorDepth(preprocessorDepth, trimmed);
        }

        return false;
    }

    private static bool TryParseUsing(string line, out ScheduleOneUsing? parsedUsing)
    {
        Match aliasMatch = AliasUsingRegex.Match(line);
        if (aliasMatch.Success)
        {
            string indent = aliasMatch.Groups["indent"].Value;
            string alias = aliasMatch.Groups["alias"].Value;
            string target = aliasMatch.Groups["target"].Value;
            parsedUsing = new ScheduleOneUsing(
                indent,
                $"using {alias} = {target};",
                $"using {alias} = Il2Cpp{target};",
                RequiresSourceUsing: true);
            return true;
        }

        Match usingMatch = NormalOrStaticUsingRegex.Match(line);
        if (usingMatch.Success)
        {
            string indent = usingMatch.Groups["indent"].Value;
            string staticKeyword = usingMatch.Groups["static"].Success ? "static " : string.Empty;
            string target = usingMatch.Groups["target"].Value;
            parsedUsing = new ScheduleOneUsing(
                indent,
                $"using {staticKeyword}{target};",
                $"using {staticKeyword}Il2Cpp{target};",
                RequiresSourceUsing: usingMatch.Groups["static"].Success);
            return true;
        }

        parsedUsing = null;
        return false;
    }

    private static IReadOnlyList<string> BuildReplacementBlock(
        string indent,
        IReadOnlyList<ScheduleOneUsing> usings,
        RewriteMode mode)
    {
        if (mode == RewriteMode.PreferGlobalFacade)
        {
            usings = usings
                .Where(usingEntry => usingEntry.RequiresSourceUsing)
                .ToArray();
            if (usings.Count == 0)
            {
                return Array.Empty<string>();
            }
        }

        var replacement = new List<string>(usings.Count * 2 + 3)
        {
            $"{indent}#if MONO"
        };
        replacement.AddRange(usings.Select(usingEntry => $"{indent}{usingEntry.MonoUsing}"));
        replacement.Add($"{indent}#elif IL2CPP");
        replacement.AddRange(usings.Select(usingEntry => $"{indent}{usingEntry.Il2CppUsing}"));
        replacement.Add($"{indent}#endif");
        return replacement;
    }

    private static int UpdatePreprocessorDepth(int currentDepth, string trimmedLine)
    {
        if (trimmedLine.StartsWith("#if", StringComparison.Ordinal))
        {
            return currentDepth + 1;
        }

        if (trimmedLine.StartsWith("#endif", StringComparison.Ordinal))
        {
            return Math.Max(0, currentDepth - 1);
        }

        return currentDepth;
    }

    private static bool IsGeneratedOrBuildOutput(string projectDirectory, string file)
    {
        return WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file);
    }

    private sealed record ScheduleOneUsing(
        string Indent,
        string MonoUsing,
        string Il2CppUsing,
        bool RequiresSourceUsing);
}
