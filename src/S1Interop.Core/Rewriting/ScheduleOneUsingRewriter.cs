using System.Text;
using System.Text.RegularExpressions;

namespace S1Interop.Core.Rewriting;

/// <summary>
/// Rewrites unconditional Mono <c>ScheduleOne</c> using directives for dual-runtime or generated-facade source.
/// </summary>
public static class ScheduleOneUsingRewriter
{
    /// <summary>
    /// Selects how Mono using directives are represented after rewriting.
    /// </summary>
    public enum RewriteMode
    {
        /// <summary>
        /// Emits conditional Mono and IL2CPP using directives.
        /// </summary>
        ConditionalizeAll,

        /// <summary>
        /// Prefers the generated S1Interop facade namespace and keeps aliases conditional when necessary.
        /// </summary>
        PreferGlobalFacade
    }

    private static readonly Regex NormalOrStaticUsingRegex = new(
        @"^(?<indent>\s*)using\s+(?<static>static\s+)?(?<target>ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;\s*$",
        RegexOptions.Compiled);

    private static readonly Regex AliasUsingRegex = new(
        @"^(?<indent>\s*)using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<target>ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Finds project C# files with unconditional <c>ScheduleOne</c> using directives outside preprocessor blocks.
    /// </summary>
    /// <param name="projectPath">The path to the owning <c>.csproj</c> file.</param>
    /// <returns>Matching source file paths ordered for stable migration output.</returns>
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

    /// <summary>
    /// Rewrites a source file in place using <see cref="RewriteMode.ConditionalizeAll"/>.
    /// </summary>
    /// <param name="filePath">The C# source file to rewrite.</param>
    /// <returns>True when the file was changed; false when it was missing or already required no changes.</returns>
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

    /// <summary>
    /// Rewrites C# source using <see cref="RewriteMode.ConditionalizeAll"/>.
    /// </summary>
    /// <param name="source">The original C# source.</param>
    /// <returns>The rewritten source.</returns>
    public static string RewriteSource(string source)
    {
        return RewriteSource(source, RewriteMode.ConditionalizeAll);
    }

    /// <summary>
    /// Rewrites unconditional Schedule I game using directives with the selected strategy.
    /// </summary>
    /// <param name="source">The original C# source.</param>
    /// <param name="mode">The rewrite strategy.</param>
    /// <returns>The rewritten source while preserving the original newline style.</returns>
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
