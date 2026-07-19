using System.Text;
using System.Xml.Linq;

namespace S1Interop.Core.Migration;

/// <summary>
/// Creates and installs the optional MSBuild hook that runs S1Interop lint before reference resolution.
/// </summary>
public static class BuildValidationHook
{
    /// <summary>
    /// Gets the generated MSBuild targets file name.
    /// </summary>
    public const string TargetFileName = "S1Interop.Build.targets";

    /// <summary>
    /// Gets the local props file name used to override the S1Interop command path.
    /// </summary>
    public const string LocalPropsFileName = "S1Interop.Build.local.props";

    /// <summary>
    /// Gets the validation targets path beside a project file.
    /// </summary>
    /// <param name="projectPath">The owning <c>.csproj</c> path.</param>
    /// <returns>The full generated targets path.</returns>
    public static string GetTargetsPath(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, TargetFileName);

    /// <summary>
    /// Gets the local validation props path beside a project file.
    /// </summary>
    /// <param name="projectPath">The owning <c>.csproj</c> path.</param>
    /// <returns>The full local props path.</returns>
    public static string GetLocalPropsPath(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, LocalPropsFileName);

    /// <summary>
    /// Adds the conditional S1Interop targets import when it is not already present.
    /// </summary>
    /// <param name="document">The project XML document to update.</param>
    /// <returns>True when an import was added; false when the import already existed.</returns>
    public static bool EnsureImport(XDocument document)
    {
        bool exists = document.Root!.Elements().Where(IsNamed("Import")).Any(import =>
            string.Equals(import.Attribute("Project")?.Value, TargetFileName, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return false;
        }

        document.Root!.Add(new XElement(
            "Import",
            new XAttribute("Project", TargetFileName),
            new XAttribute("Condition", $"Exists('{TargetFileName}')")));
        return true;
    }

    /// <summary>
    /// Generates the MSBuild target that runs <c>s1interop lint</c> before normal reference resolution.
    /// </summary>
    /// <returns>The complete MSBuild targets XML.</returns>
    public static string GenerateTargets() =>
        $$"""
          <Project>
            <Import Project="{{LocalPropsFileName}}" Condition="Exists('{{LocalPropsFileName}}')" />

            <PropertyGroup>
              <S1InteropBuildValidationEnabled Condition="'$(S1InteropBuildValidationEnabled)' == ''">true</S1InteropBuildValidationEnabled>
              <S1InteropCommand Condition="'$(S1InteropCommand)' == ''">s1interop</S1InteropCommand>
            </PropertyGroup>

            <Target Name="S1InteropValidate"
                    BeforeTargets="ResolveReferences"
                    Condition="'$(S1InteropBuildValidationEnabled)' == 'true' and '$(DesignTimeBuild)' != 'true'">
              <Message Importance="high" Text="S1Interop validating $(MSBuildProjectFile)" />
              <Exec Command="$(S1InteropCommand) lint &quot;$(MSBuildProjectFullPath)&quot; --configuration &quot;$(Configuration)&quot;" />
            </Target>
          </Project>
          """;

    /// <summary>
    /// Generates local validation props using a repository CLI project when one can be found, or the installed <c>s1interop</c> command otherwise.
    /// </summary>
    /// <returns>The complete local props XML.</returns>
    public static string GenerateLocalProps() =>
        GenerateLocalProps(ResolveDefaultCommand());

    /// <summary>
    /// Generates local validation props for an explicit S1Interop command.
    /// </summary>
    /// <param name="command">The command MSBuild should execute before the <c>lint</c> arguments.</param>
    /// <returns>The complete local props XML with the command escaped for XML.</returns>
    public static string GenerateLocalProps(string command)
    {
        string escapedCommand = EscapeXml(command);
        return $$"""
               <Project>
                 <PropertyGroup>
                   <S1InteropCommand>{{escapedCommand}}</S1InteropCommand>
                 </PropertyGroup>
               </Project>
               """;
    }

    private static string ResolveDefaultCommand()
    {
        string? cliProject = FindCliProjectPath();
        return cliProject is null
            ? "s1interop"
            : $"dotnet run --project \"{cliProject}\" --";
    }

    private static string? FindCliProjectPath()
    {
        foreach (string root in CandidateRoots())
        {
            string direct = Path.Combine(root, "src", "S1Interop.Cli", "S1Interop.Cli.csproj");
            if (File.Exists(direct))
            {
                return direct;
            }

            string nested = Path.Combine(root, "S1Interop", "src", "S1Interop.Cli", "S1Interop.Cli.csproj");
            if (File.Exists(nested))
            {
                return nested;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }
    }

    private static string EscapeXml(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => character
            });
        }

        return builder.ToString();
    }

    private static Func<XElement, bool> IsNamed(string localName) =>
        element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}
