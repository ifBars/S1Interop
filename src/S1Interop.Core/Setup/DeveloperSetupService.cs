using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core.Setup;

/// <summary>
/// Describes one prerequisite or local-configuration check.
/// </summary>
public sealed record DeveloperSetupCheck(
    string Id,
    string Status,
    string Message,
    string? Remediation = null);

/// <summary>
/// Contains the detected local inputs and checks used by the doctor and setup workflows.
/// </summary>
public sealed record DeveloperSetupReport(
    string ProjectDirectory,
    string LocalPropsPath,
    string? MonoGamePath,
    string? Il2CppGamePath,
    string? GeneratorPackageSource,
    bool Ready,
    bool CanApply,
    bool LocalPropsExists,
    IReadOnlyList<DeveloperSetupCheck> Checks);

/// <summary>
/// Inspects local Schedule I development inputs and writes the ignored local configuration used by generated projects.
/// </summary>
public sealed class DeveloperSetupService
{
    private const string LocalPropsFileName = "local.build.props";
    private static readonly Regex SteamLibraryPathRegex = new(
        @"""path""\s+""(?<path>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Detects and validates local game installs, generator packages, and local configuration safety.
    /// </summary>
    public DeveloperSetupReport Inspect(
        string path,
        string? monoGamePath = null,
        string? il2CppGamePath = null,
        string? generatorPackageSource = null)
    {
        string projectDirectory = ResolveProjectDirectory(path);
        string localPropsPath = Path.Combine(projectDirectory, LocalPropsFileName);
        IReadOnlyDictionary<string, string> existingProperties = ReadLocalProperties(localPropsPath);

        string? resolvedMonoPath = ResolveDirectory(
            monoGamePath,
            GetProperty(existingProperties, "MonoGamePath"),
            DiscoverGameInstalls().FirstOrDefault(IsMonoInstall));
        string? resolvedIl2CppPath = ResolveDirectory(
            il2CppGamePath,
            GetProperty(existingProperties, "Il2CppGamePath"),
            DiscoverGameInstalls().FirstOrDefault(IsIl2CppInstall));
        string? resolvedPackageSource = ResolveDirectory(
            generatorPackageSource,
            GetProperty(existingProperties, S1InteropPackageInfo.GeneratorsPackageSourceProperty),
            DiscoverGeneratorPackageSource(projectDirectory));

        var checks = new List<DeveloperSetupCheck>
        {
            ValidateProjectDirectory(projectDirectory),
            ValidateMonoInstall(resolvedMonoPath),
            ValidateIl2CppInstall(resolvedIl2CppPath),
            ValidateGeneratorPackageSource(resolvedPackageSource),
            ValidateIgnoreSafety(projectDirectory, localPropsPath)
        };

        bool localPropsExists = File.Exists(localPropsPath);
        if (localPropsExists)
        {
            checks.Add(new DeveloperSetupCheck(
                "local_configuration",
                "ready",
                $"Existing local configuration found at {localPropsPath}.",
                "setup will not overwrite it; edit it explicitly or move it aside before applying a new setup plan."));
        }

        bool ready = checks
            .Where(check => check.Id is "project" or "mono" or "generator_package" or "ignore_safety")
            .All(check => check.Status == "ready");
        bool canApply = ready && !localPropsExists;

        return new DeveloperSetupReport(
            projectDirectory,
            localPropsPath,
            resolvedMonoPath,
            resolvedIl2CppPath,
            resolvedPackageSource,
            ready,
            canApply,
            localPropsExists,
            checks);
    }

    /// <summary>
    /// Writes an inspected setup plan to <c>local.build.props</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when required inputs are missing, the file is not ignored, or a local configuration already exists.</exception>
    public void Apply(DeveloperSetupReport report)
    {
        if (!report.CanApply)
        {
            throw new InvalidOperationException(
                "The setup plan is not safe to apply. Resolve the reported blockers; existing local.build.props files are never overwritten.");
        }

        var document = new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "PropertyGroup",
                    new XElement("MonoGamePath", report.MonoGamePath),
                    new XElement("Il2CppGamePath", report.Il2CppGamePath ?? string.Empty),
                    new XElement(S1InteropPackageInfo.GeneratorsPackageSourceProperty, report.GeneratorPackageSource),
                    new XElement(
                        S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty,
                        new XAttribute(
                            "Condition",
                            $"'$({S1InteropPackageInfo.GeneratorsPackageSourceProperty})'!=''"),
                        $"$({S1InteropPackageInfo.GeneratorsPackageSourceProperty});$({S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty})"))));

        try
        {
            using var stream = new FileStream(
                report.LocalPropsPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            document.Save(stream);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "local.build.props already exists or could not be created safely; setup never overwrites local configuration.",
                ex);
        }
    }

    private static DeveloperSetupCheck ValidateProjectDirectory(string projectDirectory)
    {
        string[] projects = Directory.Exists(projectDirectory)
            ? Directory.GetFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        return projects.Length == 1
            ? new DeveloperSetupCheck("project", "ready", $"Project: {projects[0]}")
            : new DeveloperSetupCheck(
                "project",
                "missing",
                projects.Length == 0
                    ? $"No project file was found in {projectDirectory}."
                    : $"More than one project file was found in {projectDirectory}.",
                "Pass the path to one .csproj file or to a directory containing exactly one project.");
    }

    private static DeveloperSetupCheck ValidateMonoInstall(string? path)
    {
        string[] required =
        [
            "Schedule I.exe",
            Path.Combine("Schedule I_Data", "Managed", "Assembly-CSharp.dll"),
            Path.Combine("Schedule I_Data", "Managed", "ScheduleOne.Core.dll"),
            Path.Combine("MelonLoader", "net35", "MelonLoader.dll")
        ];
        return ValidateInstall(
            "mono",
            "Mono",
            path,
            required,
            required: true,
            "Pass --mono-game-path <path-to-alternate-install>, or launch that install after MelonLoader is installed.");
    }

    private static DeveloperSetupCheck ValidateIl2CppInstall(string? path)
    {
        string[] required =
        [
            "Schedule I.exe",
            Path.Combine("MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll"),
            Path.Combine("MelonLoader", "Il2CppAssemblies", "Il2CppScheduleOne.Core.dll"),
            Path.Combine("MelonLoader", "net6", "MelonLoader.dll")
        ];
        return ValidateInstall(
            "il2cpp",
            "IL2CPP",
            path,
            required,
            required: false,
            "Optional for the first Mono build. Pass --il2cpp-game-path <path-to-public-install> after MelonLoader generates Il2CppAssemblies.");
    }

    private static DeveloperSetupCheck ValidateInstall(
        string id,
        string displayName,
        string? path,
        IReadOnlyList<string> requiredFiles,
        bool required,
        string remediation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new DeveloperSetupCheck(
                id,
                required ? "missing" : "optional",
                $"{displayName} install was not detected.",
                remediation);
        }

        string[] missingFiles = requiredFiles
            .Where(relativePath => !File.Exists(Path.Combine(path, relativePath)))
            .ToArray();
        return missingFiles.Length == 0
            ? new DeveloperSetupCheck(id, "ready", $"{displayName} references are ready at {path}.")
            : new DeveloperSetupCheck(
                id,
                required ? "missing" : "optional",
                $"{displayName} path is incomplete at {path}. Missing: {string.Join(", ", missingFiles)}.",
                remediation);
    }

    private static DeveloperSetupCheck ValidateGeneratorPackageSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new DeveloperSetupCheck(
                "generator_package",
                "missing",
                "The local S1Interop.Generators package source was not detected.",
                "Pack S1Interop.Generators, then pass --generator-package-source <artifacts\\packages>. setup does not install or pack software.");
        }

        string packageFileName = $"{S1InteropPackageInfo.GeneratorsPackageId}.{S1InteropPackageInfo.GeneratorsPackageVersion}.nupkg";
        return File.Exists(Path.Combine(path, packageFileName))
            ? new DeveloperSetupCheck("generator_package", "ready", $"Generator package source is ready at {path}.")
            : new DeveloperSetupCheck(
                "generator_package",
                "missing",
                $"No exact {packageFileName} package was found in {path}.",
                "Run dotnet pack for S1Interop.Generators into this folder, then rerun doctor.");
    }

    private static DeveloperSetupCheck ValidateIgnoreSafety(string projectDirectory, string localPropsPath) =>
        IsIgnoredByProject(projectDirectory)
            ? new DeveloperSetupCheck("ignore_safety", "ready", $"{localPropsPath} is covered by a local .gitignore rule.")
            : new DeveloperSetupCheck(
                "ignore_safety",
                "missing",
                $"{localPropsPath} is not covered by a recognized .gitignore rule.",
                "Add an explicit local.build.props rule to the project .gitignore before using setup --apply.");

    private static bool IsIgnoredByProject(string projectDirectory)
    {
        string? directory = projectDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string gitignorePath = Path.Combine(directory, ".gitignore");
            if (File.Exists(gitignorePath) &&
                File.ReadLines(gitignorePath)
                    .Select(line => line.Trim().Replace('\\', '/'))
                    .Any(line => line is "local.build.props" or "/local.build.props" or "**/local.build.props"))
            {
                return true;
            }

            if (Directory.Exists(Path.Combine(directory, ".git")))
            {
                break;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return false;
    }

    private static string ResolveProjectDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath)!
            : fullPath;
    }

    private static IReadOnlyDictionary<string, string> ReadLocalProperties(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return XDocument.Load(path)
                .Descendants()
                .Where(element => !element.HasElements)
                .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? GetProperty(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out string? value) ? value : null;

    private static string? ResolveDirectory(params string?[] candidates) =>
        candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();

    private static IReadOnlyList<string> DiscoverGameInstalls()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            steamRoots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        foreach (string root in steamRoots.ToArray())
        {
            string libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            foreach (Match match in SteamLibraryPathRegex.Matches(File.ReadAllText(libraryFile)))
            {
                steamRoots.Add(match.Groups["path"].Value.Replace(@"\\", @"\", StringComparison.Ordinal));
            }
        }

        return steamRoots
            .Select(root => Path.Combine(root, "steamapps", "common"))
            .Where(Directory.Exists)
            .SelectMany(common => Directory.EnumerateDirectories(common, "Schedule I*", SearchOption.TopDirectoryOnly))
            .Where(directory => File.Exists(Path.Combine(directory, "Schedule I.exe")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsMonoInstall(string path) =>
        File.Exists(Path.Combine(path, "Schedule I_Data", "Managed", "ScheduleOne.Core.dll"));

    private static bool IsIl2CppInstall(string path) =>
        File.Exists(Path.Combine(path, "MelonLoader", "Il2CppAssemblies", "Il2CppScheduleOne.Core.dll"));

    private static string? DiscoverGeneratorPackageSource(string projectDirectory)
    {
        string[] candidates =
        [
            Path.Combine(projectDirectory, "artifacts", "packages"),
            Path.GetFullPath(Path.Combine(projectDirectory, "..", "S1Interop", "artifacts", "packages")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "packages"))
        ];
        return candidates.FirstOrDefault(path =>
            Directory.Exists(path) &&
            Directory.EnumerateFiles(
                path,
                $"{S1InteropPackageInfo.GeneratorsPackageId}.{S1InteropPackageInfo.GeneratorsPackageVersion}.nupkg",
                SearchOption.TopDirectoryOnly).Any());
    }
}
