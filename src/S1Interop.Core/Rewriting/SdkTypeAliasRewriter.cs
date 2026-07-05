using System.Text;
using System.Text.RegularExpressions;

namespace S1Interop.Core.Rewriting;

public static class SdkTypeAliasRewriter
{
    private enum RewriteState
    {
        Code,
        BlockComment,
        RegularString,
        VerbatimString,
        CharLiteral
    }

    public static IReadOnlyList<string> FindFilesWithRewritableTypeAliases(string projectPath, IReadOnlyList<SdkTypeAlias> aliases)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory) || aliases.Count == 0)
        {
            return Array.Empty<string>();
        }

        return WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => !WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file))
            .Where(file => !IsGeneratedFacade(file))
            .Where(file => CanRewriteSource(File.ReadAllText(file), aliases))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string RewriteSource(string source, IReadOnlyList<SdkTypeAlias> aliases)
    {
        if (aliases.Count == 0)
        {
            return source;
        }

        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        RewriteState state = RewriteState.Code;
        for (int index = 0; index < lines.Length; index++)
        {
            if (state == RewriteState.Code && lines[index].TrimStart().StartsWith("using ", StringComparison.Ordinal))
            {
                continue;
            }

            lines[index] = RewriteLine(lines[index], aliases, ref state);
        }

        return string.Join(newline, lines);
    }

    private static bool CanRewriteSource(string source, IReadOnlyList<SdkTypeAlias> aliases) =>
        !string.Equals(source, RewriteSource(source, aliases), StringComparison.Ordinal);

    private static string RewriteLine(string line, IReadOnlyList<SdkTypeAlias> aliases, ref RewriteState state)
    {
        var rewritten = new StringBuilder(line.Length);
        var code = new StringBuilder();

        for (int index = 0; index < line.Length; index++)
        {
            char ch = line[index];
            char next = index + 1 < line.Length ? line[index + 1] : '\0';

            switch (state)
            {
                case RewriteState.Code:
                    if (ch == '/' && next == '/')
                    {
                        AppendRewrittenCode(rewritten, code, aliases);
                        rewritten.Append(line, index, line.Length - index);
                        index = line.Length;
                    }
                    else if (ch == '/' && next == '*')
                    {
                        AppendRewrittenCode(rewritten, code, aliases);
                        rewritten.Append("/*");
                        index++;
                        state = RewriteState.BlockComment;
                    }
                    else if (ch == '"')
                    {
                        AppendRewrittenCode(rewritten, code, aliases);
                        rewritten.Append(ch);
                        state = IsVerbatimStringStart(line, index)
                            ? RewriteState.VerbatimString
                            : RewriteState.RegularString;
                    }
                    else if (ch == '\'')
                    {
                        AppendRewrittenCode(rewritten, code, aliases);
                        rewritten.Append(ch);
                        state = RewriteState.CharLiteral;
                    }
                    else
                    {
                        code.Append(ch);
                    }

                    break;

                case RewriteState.BlockComment:
                    rewritten.Append(ch);
                    if (ch == '*' && next == '/')
                    {
                        rewritten.Append(next);
                        index++;
                        state = RewriteState.Code;
                    }

                    break;

                case RewriteState.RegularString:
                    rewritten.Append(ch);
                    if (ch == '\\' && next != '\0')
                    {
                        rewritten.Append(next);
                        index++;
                    }
                    else if (ch == '"')
                    {
                        state = RewriteState.Code;
                    }

                    break;

                case RewriteState.VerbatimString:
                    rewritten.Append(ch);
                    if (ch == '"' && next == '"')
                    {
                        rewritten.Append(next);
                        index++;
                    }
                    else if (ch == '"')
                    {
                        state = RewriteState.Code;
                    }

                    break;

                case RewriteState.CharLiteral:
                    rewritten.Append(ch);
                    if (ch == '\\' && next != '\0')
                    {
                        rewritten.Append(next);
                        index++;
                    }
                    else if (ch == '\'')
                    {
                        state = RewriteState.Code;
                    }

                    break;
            }
        }

        AppendRewrittenCode(rewritten, code, aliases);
        return rewritten.ToString();
    }

    private static void AppendRewrittenCode(StringBuilder rewritten, StringBuilder code, IReadOnlyList<SdkTypeAlias> aliases)
    {
        if (code.Length == 0)
        {
            return;
        }

        string segment = code.ToString();
        foreach (SdkTypeAlias alias in aliases)
        {
            segment = ReplaceTypeofToken(segment, alias.MonoType, alias.Alias);
            segment = ReplaceTypeofToken(segment, alias.Il2CppType, alias.Alias);
            segment = ReplaceTypeToken(segment, alias.MonoType, alias.Alias);
            segment = ReplaceTypeToken(segment, alias.Il2CppType, alias.Alias);
        }

        rewritten.Append(segment);
        code.Clear();
    }

    private static bool IsVerbatimStringStart(string line, int quoteIndex)
    {
        int previous = quoteIndex - 1;
        if (previous < 0)
        {
            return false;
        }

        if (line[previous] == '@')
        {
            return true;
        }

        return line[previous] == '$' && previous - 1 >= 0 && line[previous - 1] == '@';
    }

    private static string ReplaceTypeToken(string code, string typeName, string alias)
    {
        string pattern = $@"(?<![A-Za-z0-9_\.]){Regex.Escape(typeName)}(?![A-Za-z0-9_])";
        return Regex.Replace(code, pattern, alias);
    }

    private static string ReplaceTypeofToken(string code, string typeName, string alias)
    {
        string pattern = $@"\btypeof\s*\(\s*{Regex.Escape(typeName)}\s*\)";
        return Regex.Replace(code, pattern, $"S1Interop.Generated.S1InteropTypeRegistry.{alias}");
    }

    private static bool IsGeneratedFacade(string file) =>
        Path.GetFileName(file).Equals("S1Interop.GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase);
}
