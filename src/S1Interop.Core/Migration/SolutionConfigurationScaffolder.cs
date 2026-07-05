using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core.Migration;

internal static class SolutionConfigurationScaffolder
{
    public static IReadOnlyList<SolutionConfigurationScaffoldResult> Scaffold(string projectPath, XDocument document)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string projectFileName = Path.GetFileName(projectPath);
        string[] configurations = GetProjectConfigurations(document).ToArray();
        if (configurations.Length == 0)
        {
            return [];
        }

        var results = new List<SolutionConfigurationScaffoldResult>();
        foreach (string solutionPath in Directory.EnumerateFiles(projectDirectory, "*.sln", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(solutionPath);
            string? projectGuid = FindSolutionProjectGuid(text, projectFileName);
            if (projectGuid is null)
            {
                continue;
            }

            string updated = EnsureSolutionConfigurations(text, projectGuid, configurations);
            if (!string.Equals(text, updated, StringComparison.Ordinal))
            {
                results.Add(new SolutionConfigurationScaffoldResult(solutionPath, updated));
            }
        }

        return results;
    }

    private static IEnumerable<string> GetProjectConfigurations(XDocument document)
    {
        var configurations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement element in document.Descendants().Where(IsNamed("Configurations")))
        {
            foreach (string configuration in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                configurations.Add(configuration);
            }
        }

        return configurations;
    }

    private static string? FindSolutionProjectGuid(string solutionText, string projectFileName)
    {
        foreach (Match match in Regex.Matches(
                     solutionText,
                     @"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""(?<path>[^""]+)"",\s*""(?<guid>\{[^}]+\})""",
                     RegexOptions.IgnoreCase))
        {
            string path = match.Groups["path"].Value.Replace('/', '\\');
            if (string.Equals(Path.GetFileName(path), projectFileName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["guid"].Value;
            }
        }

        return null;
    }

    private static string EnsureSolutionConfigurations(string solutionText, string projectGuid, IReadOnlyList<string> configurations)
    {
        string updated = EnsureSolutionGlobalSection(
            solutionText,
            "SolutionConfigurationPlatforms",
            "preSolution",
            configurations.Select(configuration => $"\t\t{configuration}|Any CPU = {configuration}|Any CPU"));

        return EnsureSolutionGlobalSection(
            updated,
            "ProjectConfigurationPlatforms",
            "postSolution",
            configurations.SelectMany(configuration => new[]
            {
                $"\t\t{projectGuid}.{configuration}|Any CPU.ActiveCfg = {configuration}|Any CPU",
                $"\t\t{projectGuid}.{configuration}|Any CPU.Build.0 = {configuration}|Any CPU"
            }));
    }

    private static string EnsureSolutionGlobalSection(
        string solutionText,
        string sectionName,
        string sectionKind,
        IEnumerable<string> requiredLines)
    {
        string normalized = solutionText.Replace("\r\n", "\n", StringComparison.Ordinal);
        string header = $"\tGlobalSection({sectionName}) = {sectionKind}";
        int start = normalized.IndexOf(header, StringComparison.Ordinal);
        if (start < 0)
        {
            int globalEnd = normalized.LastIndexOf("EndGlobal", StringComparison.Ordinal);
            if (globalEnd < 0)
            {
                return solutionText;
            }

            string newSection = header + "\n" + string.Join("\n", requiredLines) + "\n\tEndGlobalSection\n";
            normalized = normalized.Insert(globalEnd, newSection);
            return normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }

        int end = normalized.IndexOf("\n\tEndGlobalSection", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return solutionText;
        }

        int sectionEnd = normalized.IndexOf('\n', end + 1);
        if (sectionEnd < 0)
        {
            sectionEnd = normalized.Length;
        }

        string section = normalized[start..sectionEnd];
        var existing = new HashSet<string>(
            section.Split('\n').Select(line => line.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var additions = requiredLines
            .Where(line => !existing.Contains(line.Trim()))
            .ToArray();
        if (additions.Length == 0)
        {
            return solutionText;
        }

        string updatedSection = section.Insert(section.LastIndexOf("\n\tEndGlobalSection", StringComparison.Ordinal), "\n" + string.Join("\n", additions));
        normalized = normalized.Remove(start, section.Length).Insert(start, updatedSection);
        return normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static Func<XElement, bool> IsNamed(string localName) =>
        element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}

internal sealed record SolutionConfigurationScaffoldResult(string SolutionPath, string UpdatedText);
