using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core.Migration;

/// <summary>
/// Verifies migrations by applying planned operations to disposable sandbox copies.
/// </summary>
/// <remarks>
/// Verification copies project files into a temporary sandbox, applies migration operations there, and attempts to delete the sandbox before returning.
/// The original project is not mutated by this verifier.
/// </remarks>
public sealed class MigrationVerifier
{
    private const string SandboxPrefix = "S1Interop.Verify.";
    private const int MaxVerificationPasses = 8;
    private const int MaxBuildOutputChars = 40000;
    private const int MaxBuildIssues = 40;
    private const string ExternalReferenceStagingDirectoryName = "S1Interop.ExternalReferences";

    private static readonly Regex MissingPackageRegex = new(
        @"Unable to find package (?<id>.+?)\. No packages exist",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MissingPackageSourcesRegex = new(
        @"No packages exist with this id in source\(s\):\s*(?<sources>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex MissingReferenceOutputRegex = new(
        @"assembly\s+""(?<include>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] KnownExternalNamespaceHints =
    [
        "Il2CppScheduleOne",
        "ScheduleOne",
        "UnityEngine",
        "TMPro",
        "FishNet",
        "MelonLoader",
        "HarmonyLib",
        "Il2CppInterop"
    ];

    private static readonly string[] KnownExternalMemberTypeHints =
    [
        "PlayerCamera",
        "PlayerMovement",
        "PlayerInventory",
        "HUD",
        "NetworkBehaviour",
        "NPC",
        "Property",
        "Vehicle",
        "Shop",
        "Station",
        "Registry",
        "GameManager",
        "ScheduleOne",
        "Il2CppScheduleOne",
        "UnityEngine",
        "FishNet",
        "MelonLoader",
        "Il2Cpp"
    ];

    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        WorkspaceTraversal.CommonExcludedDirectoryNames,
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> WindowsReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private static readonly HashSet<string> ExcludedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z",
        ".asset",
        ".assets",
        ".bank",
        ".bundle",
        ".dll",
        ".dylib",
        ".exe",
        ".fbx",
        ".glb",
        ".gltf",
        ".jpeg",
        ".jpg",
        ".mdb",
        ".mp3",
        ".obj",
        ".ogg",
        ".pdb",
        ".prefab",
        ".psd",
        ".rar",
        ".so",
        ".tga",
        ".unity",
        ".wav",
        ".zip"
    };

    private readonly WorkspaceAnalyzer analyzer = new();
    private readonly MigrationPlanner planner = new();
    private readonly MigrationApplier applier = new();

    private sealed record StagedS1InteropGeneratorPackage(string SourcePath, string Version);

    /// <summary>
    /// Verifies a single project using default verification options.
    /// </summary>
    /// <param name="path">A project path or directory that resolves to exactly one project.</param>
    /// <returns>The sandbox verification result for the project.</returns>
    public MigrationVerificationResult Verify(string path) =>
        Verify(path, MigrationVerifierOptions.Default);

    /// <summary>
    /// Verifies a single project using explicit verification options.
    /// </summary>
    /// <param name="path">A project path or directory that resolves to exactly one project.</param>
    /// <param name="options">Options that control planning mode, source migrations, sandbox builds, timeouts, and local game paths.</param>
    /// <returns>The sandbox verification result for the project.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="path"/> does not resolve to exactly one project.</exception>
    public MigrationVerificationResult Verify(string path, MigrationVerifierOptions options)
    {
        WorkspaceAnalysis sourceAnalysis = analyzer.Analyze(path);
        ProjectAnalysis sourceProject = RequireSingleProject(sourceAnalysis, path);
        return VerifyProject(sourceProject.ProjectPath, options);
    }

    /// <summary>
    /// Verifies every discovered project in a workspace using default verification options.
    /// </summary>
    /// <param name="path">A workspace directory or project path to verify.</param>
    /// <returns>The aggregate verification result for all discovered projects.</returns>
    public WorkspaceMigrationVerificationResult VerifyWorkspace(string path) =>
        VerifyWorkspace(path, MigrationVerifierOptions.Default);

    /// <summary>
    /// Verifies every discovered project in a workspace using explicit verification options.
    /// </summary>
    /// <param name="path">A workspace directory or project path to verify.</param>
    /// <param name="options">Options that control planning mode, source migrations, sandbox builds, timeouts, and local game paths.</param>
    /// <returns>The aggregate verification result for all discovered projects.</returns>
    public WorkspaceMigrationVerificationResult VerifyWorkspace(string path, MigrationVerifierOptions options)
    {
        WorkspaceAnalysis sourceAnalysis = analyzer.Analyze(path);
        MigrationVerificationResult[] results = sourceAnalysis.Projects
            .Select(project => VerifyProject(project.ProjectPath, options))
            .ToArray();

        return new WorkspaceMigrationVerificationResult(
            sourceAnalysis.RootPath,
            results.Length > 0 && results.All(result => result.Success && result.SandboxDeleted),
            results.Length,
            results.Sum(result => result.PlannedOperations),
            results.Sum(result => result.AppliedOperations),
            results);
    }

    private MigrationVerificationResult VerifyProject(string projectPath, MigrationVerifierOptions options)
    {
        WorkspaceAnalysis sourceAnalysis = analyzer.Analyze(projectPath);
        ProjectAnalysis sourceProject = RequireSingleProject(sourceAnalysis, projectPath);
        string sourceProjectDirectory = Path.GetDirectoryName(sourceProject.ProjectPath)!;
        string sourceRoot = FindSandboxSourceRoot(sourceProject.ProjectPath);
        string relativeProjectPath = Path.GetRelativePath(sourceRoot, sourceProject.ProjectPath);
        string sandboxRoot = CreateSandboxRoot();
        string sandboxProjectPath = Path.Combine(sandboxRoot, relativeProjectPath);

        MigrationVerificationResult? result = null;
        bool sandboxDeleted = false;
        try
        {
            CopyProjectDirectory(sourceRoot, sandboxRoot);
            StageAncestorNuGetConfig(sourceProject.ProjectPath, sourceRoot, sandboxRoot);

            WorkspaceAnalysis before = analyzer.Analyze(sandboxProjectPath);
            WorkspaceAnalysis after = before;
            int plannedOperations = 0;
            int appliedOperations = 0;
            string? manifestPath = null;
            for (int pass = 0; pass < MaxVerificationPasses; pass++)
            {
                MigrationPlan plan = planner.Plan(
                    after,
                    new MigrationPlannerOptions(
                        options.DualRuntime,
                        IncludeSourceRisks: options.IncludeSourceMigrations));
                int passPlannedOperations = plan.Projects.Sum(project => project.Operations.Count);
                if (passPlannedOperations == 0)
                {
                    break;
                }

                plannedOperations += passPlannedOperations;
                MigrationApplyResult applyResult = applier.Apply(plan);
                appliedOperations += applyResult.Operations.Count;
                manifestPath = applyResult.ManifestPath;
                WorkspaceAnalysis next = analyzer.Analyze(sandboxProjectPath);
                after = next;
                if (next.Diagnostics.Count == 0 ||
                    applyResult.Operations.Count == 0)
                {
                    break;
                }
            }

            bool hydratedLocalProps = HydrateSandboxLocalBuildProps(sandboxProjectPath, options);
            bool stagedWorkspaceDependencies = StageWorkspaceDependencyReferences(
                sandboxProjectPath,
                sourceProjectDirectory,
                options);
            bool stagedIl2CppManagedOverlay = StageIl2CppManagedReferenceOverlay(
                sandboxProjectPath,
                sourceProjectDirectory,
                options);
            if (hydratedLocalProps || stagedWorkspaceDependencies || stagedIl2CppManagedOverlay)
            {
                after = analyzer.Analyze(sandboxProjectPath);
            }

            bool stagedConfigurationDependencies = StageConfigurationScopedWorkspaceDependencyReferences(
                sandboxProjectPath,
                sourceProjectDirectory,
                after,
                options);
            if (stagedConfigurationDependencies)
            {
                after = analyzer.Analyze(sandboxProjectPath);
            }

            bool stagedProjectLocalReferences = StageProjectLocalReferenceFiles(
                sandboxProjectPath,
                sourceProjectDirectory,
                after,
                options);
            if (stagedProjectLocalReferences)
            {
                after = analyzer.Analyze(sandboxProjectPath);
            }

            IReadOnlyList<MigrationBuildResult> buildResults = after.Diagnostics.Count == 0 && options.Build
                ? BuildMigratedProject(sandboxProjectPath, after, options)
                : Array.Empty<MigrationBuildResult>();
            bool success = after.Diagnostics.Count == 0 &&
                           (!options.Build || buildResults.All(result => result.Success));

            result = new MigrationVerificationResult(
                sourceProject.ProjectPath,
                sandboxProjectPath,
                success,
                SandboxDeleted: false,
                plannedOperations,
                appliedOperations,
                manifestPath,
                before.Diagnostics,
                after.Diagnostics,
                buildResults);
        }
        finally
        {
            sandboxDeleted = TryDeleteSandbox(sandboxRoot);
        }

        return result! with { SandboxDeleted = sandboxDeleted };
    }

    private static IReadOnlyList<MigrationBuildResult> BuildMigratedProject(
        string sandboxProjectPath,
        WorkspaceAnalysis after,
        MigrationVerifierOptions options)
    {
        ProjectAnalysis project = RequireSingleProject(after, sandboxProjectPath);
        ConfigurationAnalysis[] configurations = project.Configurations
            .OrderBy(configuration => configuration.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configurations.Length == 0)
        {
            configurations =
            [
                new ConfigurationAnalysis(
                    "Debug",
                    RuntimeKind.Unknown,
                    MonoScore: 0,
                    Il2CppScore: 0,
                    CrossCompatScore: 0,
                    TargetFramework: null,
                    DefineConstants: Array.Empty<string>(),
                    References: Array.Empty<ReferenceInfo>(),
                    PackageReferences: Array.Empty<PackageReferenceInfo>(),
                    Evidence: Array.Empty<string>())
            ];
        }

        StagedS1InteropGeneratorPackage? s1InteropPackageSource = StageS1InteropGeneratorPackageSource(sandboxProjectPath, project);
        return configurations
            .Select(configuration => BuildConfiguration(sandboxProjectPath, configuration, options, s1InteropPackageSource))
            .ToArray();
    }

    private static MigrationBuildResult BuildConfiguration(
        string projectPath,
        ConfigurationAnalysis configuration,
        MigrationVerifierOptions options,
        StagedS1InteropGeneratorPackage? additionalRestoreSource)
    {
        MigrationBuildIssue[] readinessIssues = AnalyzeBuildReadiness(projectPath, configuration, options);
        List<string> arguments =
        [
            "msbuild",
            projectPath,
            "-restore",
            "-t:Build",
            $"-p:Configuration={configuration.Name}",
            "--nologo",
            "-v:minimal",
            "-p:AutomateLocalDeployment=false",
            "-p:S1InteropBuildValidationEnabled=false",
            "-p:RunPostBuildEvent=Never",
            "-p:DeployOnBuild=false",
            "-p:BuildProjectReferences=false"
        ];
        if (additionalRestoreSource is not null)
        {
            arguments.Add($"-p:RestoreAdditionalProjectSources={additionalRestoreSource.SourcePath}");
            arguments.Add($"-p:RestorePackagesPath={Path.Combine(Path.GetDirectoryName(additionalRestoreSource.SourcePath)!, "Packages")}");
            arguments.Add("-p:RestoreNoCache=true");
        }

        if (!string.IsNullOrWhiteSpace(configuration.TargetFramework))
        {
            arguments.Add($"-p:TargetFramework={configuration.TargetFramework}");
        }

        arguments.AddRange(GetBuildPropertyArguments(projectPath, configuration.Runtime, options));
        string command = $"dotnet {string.Join(' ', arguments.Select(QuoteArgument))}";
        if (readinessIssues.Length > 0)
        {
            (string blockedReadinessStatus, string blockedAttribution) = ClassifyBuildAttribution(
                success: false,
                timedOut: false,
                readinessIssues,
                outputIssues: Array.Empty<MigrationBuildIssue>(),
                classifiedKind: "ReadinessBlocked");
            MigrationBuildIssue firstIssue = readinessIssues[0];
            return new MigrationBuildResult(
                configuration.Name,
                configuration.Runtime,
                Success: false,
                TimedOut: false,
                ExitCode: -1,
                blockedReadinessStatus,
                blockedAttribution,
                firstIssue.Kind,
                firstIssue.Message,
                readinessIssues,
                command,
                "Build skipped because readiness checks found missing local references.");
        }

        DeleteSandboxBuildOutputs(projectPath);

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                AppendBoundedLine(output, args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                AppendBoundedLine(output, args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        int timeoutMilliseconds = Math.Max(1, options.BuildTimeoutSeconds) * 1000;
        bool exited = process.WaitForExit(timeoutMilliseconds);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup; the timed-out build result still records failure.
            }
        }
        else
        {
            process.WaitForExit();
        }

        int exitCode = exited ? process.ExitCode : -1;
        string capturedOutput = output.ToString().Trim();
        bool compileSucceeded = exited && exitCode == 0;
        (string classifiedKind, string classifiedSummary) = ClassifyBuildResult(
            compileSucceeded,
            !exited,
            capturedOutput,
            configuration.Runtime);
        MigrationBuildIssue[] outputIssues = EnrichPackageIssues(
            configuration,
            FilterOutputIssues(
            readinessIssues,
            CreateOutputIssues(classifiedKind, classifiedSummary, capturedOutput)));
        MigrationBuildIssue[] issues = readinessIssues
            .Concat(outputIssues)
            .Take(MaxBuildIssues)
            .ToArray();
        bool success = compileSucceeded && readinessIssues.Length == 0;
        (string readinessStatus, string attribution) = ClassifyBuildAttribution(
            success,
            !exited,
            readinessIssues,
            outputIssues,
            classifiedKind);
        string failureKind = success ? "None" : issues.FirstOrDefault()?.Kind ?? classifiedKind;
        string summary = success ? "Build target succeeded." : issues.FirstOrDefault()?.Message ?? classifiedSummary;
        return new MigrationBuildResult(
            configuration.Name,
            configuration.Runtime,
            success,
            TimedOut: !exited,
            exitCode,
            readinessStatus,
            attribution,
            failureKind,
            summary,
            issues,
            command,
            capturedOutput);
    }

    private static StagedS1InteropGeneratorPackage? StageS1InteropGeneratorPackageSource(string sandboxProjectPath, ProjectAnalysis project)
    {
        if (!project.Configurations.Any(configuration =>
                configuration.PackageReferences.Any(package =>
                    string.Equals(package.Include, S1InteropPackageInfo.GeneratorsPackageId, StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        string? generatorProjectPath = FindS1InteropGeneratorProject(Path.GetDirectoryName(sandboxProjectPath)!);
        if (generatorProjectPath is null)
        {
            return null;
        }

        string packageSource = Path.Combine(
            Path.GetDirectoryName(sandboxProjectPath)!,
            ExternalReferenceStagingDirectoryName,
            "NuGet");
        Directory.CreateDirectory(packageSource);
        string packageVersion = S1InteropPackageInfo.CreateLocalGeneratorsPackageVersion(DateTimeOffset.UtcNow);
        RewriteSandboxGeneratorPackageVersion(sandboxProjectPath, packageVersion);

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.ArgumentList.Add("pack");
        process.StartInfo.ArgumentList.Add(generatorProjectPath);
        process.StartInfo.ArgumentList.Add("--configuration");
        process.StartInfo.ArgumentList.Add("Debug");
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(packageSource);
        process.StartInfo.ArgumentList.Add($"-p:PackageVersion={packageVersion}");
        process.StartInfo.ArgumentList.Add($"-p:Version={packageVersion}");
        process.StartInfo.ArgumentList.Add("-v:minimal");
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(60000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Build verification will still report the package restore failure if staging failed.
            }

            return null;
        }

        return process.ExitCode == 0 &&
               Directory.EnumerateFiles(packageSource, $"{S1InteropPackageInfo.GeneratorsPackageId}.{packageVersion}.nupkg").Any()
            ? new StagedS1InteropGeneratorPackage(packageSource, packageVersion)
            : null;
    }

    private static void RewriteSandboxGeneratorPackageVersion(string sandboxProjectPath, string packageVersion)
    {
        XDocument document = XDocument.Load(sandboxProjectPath, LoadOptions.PreserveWhitespace);
        bool changed = false;
        foreach (XElement package in document.Descendants().Where(IsNamed("PackageReference")))
        {
            if (!string.Equals(package.Attribute("Include")?.Value, S1InteropPackageInfo.GeneratorsPackageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            package.SetAttributeValue("Version", packageVersion);
            changed = true;
        }

        if (changed)
        {
            document.Save(sandboxProjectPath);
        }
    }

    private static string? FindS1InteropGeneratorProject(string sandboxProjectDirectory)
    {
        foreach (string anchor in GetS1InteropSearchAnchors(sandboxProjectDirectory))
        {
            string candidate = Path.Combine(anchor, "src", "S1Interop.Generators", "S1Interop.Generators.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetS1InteropSearchAnchors(string sandboxProjectDirectory)
    {
        foreach (string ancestor in EnumerateAncestors(Directory.GetCurrentDirectory()).Take(8))
        {
            yield return ancestor;
        }

        foreach (string ancestor in EnumerateAncestors(AppContext.BaseDirectory).Take(8))
        {
            yield return ancestor;
        }

        foreach (string ancestor in EnumerateAncestors(sandboxProjectDirectory).Take(4))
        {
            yield return ancestor;
        }
    }

    private static MigrationBuildIssue[] AnalyzeBuildReadiness(
        string projectPath,
        ConfigurationAnalysis configuration,
        MigrationVerifierOptions options)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        var issues = new List<MigrationBuildIssue>();

        foreach (ReferenceInfo reference in configuration.References)
        {
            if (string.IsNullOrWhiteSpace(reference.HintPath))
            {
                if (IsExternalModdingReference(reference.Include))
                {
                    issues.Add(new MigrationBuildIssue(
                        "MissingReference",
                        $"Reference '{reference.Include}' has no HintPath for {configuration.Name}; S1Interop cannot verify the local assembly file before compile.",
                        reference.Include,
                        null,
                        reference.SourcePath,
                        reference.Imported,
                        "Add a HintPath, package reference, or imported props/targets entry that points to the local modding dependency."));
                }

                continue;
            }

            bool isRootRelativeHintPath = IsRootRelativePath(reference.HintPath);
            string resolvedHintPath = ResolveHintPath(projectDirectory, reference.HintPath, configuration.Runtime, options);
            if (File.Exists(resolvedHintPath))
            {
                continue;
            }

            string kind = isRootRelativeHintPath
                ? "LocalBuildPropertiesUnset"
                : GetMissingReferenceKind(reference, resolvedHintPath);
            issues.Add(new MigrationBuildIssue(
                kind,
                GetMissingReferenceMessage(kind, reference, configuration.Name, resolvedHintPath),
                reference.Include,
                resolvedHintPath,
                reference.SourcePath,
                reference.Imported,
                GetMissingReferenceRemediation(kind)));
        }

        return CollapseReadinessIssues(configuration, issues);
    }

    private static bool HydrateSandboxLocalBuildProps(string sandboxProjectPath, MigrationVerifierOptions options)
    {
        string localPropsPath = Path.Combine(Path.GetDirectoryName(sandboxProjectPath)!, "local.build.props");
        if (!File.Exists(localPropsPath))
        {
            return false;
        }

        XDocument document = XDocument.Load(localPropsPath, LoadOptions.PreserveWhitespace);
        bool changed = false;
        foreach (XElement property in document.Descendants().Where(element => !element.HasElements))
        {
            string name = property.Name.LocalName;
            string currentValue = property.Value.Trim();
            string? value = GetKnownLocalPropertyValue(name, options);
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(currentValue, value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            property.Value = value;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        document.Save(localPropsPath);
        return true;
    }

    private static bool StageWorkspaceDependencyReferences(
        string sandboxProjectPath,
        string sourceProjectDirectory,
        MigrationVerifierOptions options)
    {
        string projectDirectory = Path.GetDirectoryName(sandboxProjectPath)!;
        string localPropsPath = Path.Combine(projectDirectory, "local.build.props");
        if (!File.Exists(localPropsPath))
        {
            return false;
        }

        XDocument document = XDocument.Load(localPropsPath, LoadOptions.PreserveWhitespace);
        Dictionary<string, string[]> expectedFilesByProperty = GetExpectedDependencyFilesByProperty(sandboxProjectPath);
        if (expectedFilesByProperty.Count == 0)
        {
            return false;
        }

        string[] searchRoots = GetWorkspaceDependencySearchRoots(sourceProjectDirectory);
        if (searchRoots.Length == 0)
        {
            return false;
        }

        string stagingRoot = Path.Combine(projectDirectory, ExternalReferenceStagingDirectoryName);
        bool changed = false;
        foreach (XElement property in document.Descendants().Where(element => !element.HasElements))
        {
            string propertyName = property.Name.LocalName;
            if (!expectedFilesByProperty.TryGetValue(propertyName, out string[]? expectedFiles) ||
                expectedFiles is null)
            {
                continue;
            }

            string currentValue = property.Value.Trim();
            if (ExpectedFilesExist(currentValue, expectedFiles))
            {
                continue;
            }

            string stageDirectory = Path.Combine(stagingRoot, propertyName);
            bool stagedAny = false;
            foreach (string expectedFile in expectedFiles)
            {
                if (TryFindWorkspaceDependency(expectedFile, searchRoots, options, out string? sourceFile) &&
                    sourceFile is not null)
                {
                    Directory.CreateDirectory(stageDirectory);
                    File.Copy(sourceFile, Path.Combine(stageDirectory, expectedFile), overwrite: true);
                    stagedAny = true;
                }
            }

            if (!stagedAny)
            {
                continue;
            }

            property.Value = stageDirectory;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        document.Save(localPropsPath);
        return true;
    }

    private static bool StageIl2CppManagedReferenceOverlay(
        string sandboxProjectPath,
        string sourceProjectDirectory,
        MigrationVerifierOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Il2CppGamePath))
        {
            return false;
        }

        string projectDirectory = Path.GetDirectoryName(sandboxProjectPath)!;
        string localPropsPath = Path.Combine(projectDirectory, "local.build.props");
        if (!File.Exists(localPropsPath))
        {
            return false;
        }

        string il2CppAssembliesPath = Path.Combine(
            Path.GetFullPath(options.Il2CppGamePath),
            "MelonLoader",
            "Il2CppAssemblies");
        if (!Directory.Exists(il2CppAssembliesPath))
        {
            return false;
        }

        string[] expectedFiles = GetExpectedIl2CppManagedReferenceFiles(sandboxProjectPath);
        if (expectedFiles.Length == 0 ||
            expectedFiles.All(file => File.Exists(Path.Combine(il2CppAssembliesPath, file))))
        {
            return false;
        }

        string[] searchRoots = GetWorkspaceDependencySearchRoots(sourceProjectDirectory);
        if (searchRoots.Length == 0)
        {
            return false;
        }

        string overlayDirectory = Path.Combine(
            projectDirectory,
            ExternalReferenceStagingDirectoryName,
            "Il2CppManagedDllPath");
        Directory.CreateDirectory(overlayDirectory);

        bool copiedAny = false;
        foreach (string expectedFile in expectedFiles)
        {
            string overlayFile = Path.Combine(overlayDirectory, expectedFile);
            string gameFile = Path.Combine(il2CppAssembliesPath, expectedFile);
            if (File.Exists(gameFile))
            {
                File.Copy(gameFile, overlayFile, overwrite: true);
                copiedAny = true;
                continue;
            }

            if (TryFindWorkspaceDependency(expectedFile, searchRoots, options, out string? sourceFile) &&
                sourceFile is not null)
            {
                File.Copy(sourceFile, overlayFile, overwrite: true);
                copiedAny = true;
            }
        }

        if (!copiedAny)
        {
            return false;
        }

        XDocument document = XDocument.Load(localPropsPath, LoadOptions.PreserveWhitespace);
        bool changed = false;
        foreach (string propertyName in new[] { "ManagedDllPath", "Il2CppManagedDllPath", "Il2CppManagedPath" })
        {
            XElement? property = document.Descendants()
                .FirstOrDefault(element =>
                    !element.HasElements &&
                    element.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (property is null ||
                property.Value.Trim().Equals(overlayDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            property.Value = overlayDirectory;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        document.Save(localPropsPath);
        return true;
    }

    private static bool StageConfigurationScopedWorkspaceDependencyReferences(
        string sandboxProjectPath,
        string sourceProjectDirectory,
        WorkspaceAnalysis analysis,
        MigrationVerifierOptions options)
    {
        ProjectAnalysis project = RequireSingleProject(analysis, sandboxProjectPath);
        string projectDirectory = Path.GetDirectoryName(sandboxProjectPath)!;
        string[] searchRoots = GetWorkspaceDependencySearchRoots(sourceProjectDirectory);
        if (searchRoots.Length == 0)
        {
            return false;
        }

        var overrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (ConfigurationAnalysis configuration in project.Configurations)
        {
            foreach (ReferenceInfo reference in configuration.References)
            {
                if (string.IsNullOrWhiteSpace(reference.HintPath) ||
                    !TryGetReferencePropertyBinding(
                        sandboxProjectPath,
                        reference,
                        configuration.Name,
                        out ReferencePropertyBinding binding))
                {
                    continue;
                }

                string resolvedHintPath = ResolveHintPath(projectDirectory, reference.HintPath, configuration.Runtime, options);
                if (File.Exists(resolvedHintPath))
                {
                    continue;
                }

                string expectedFile = GetExpectedReferenceFile(reference, resolvedHintPath, binding);
                if (string.IsNullOrWhiteSpace(expectedFile) ||
                    !ShouldStageWorkspaceReference(reference, resolvedHintPath, binding) ||
                    !TryFindWorkspaceDependency(expectedFile, searchRoots, options, configuration.Runtime, out string? sourceFile) ||
                    sourceFile is null)
                {
                    continue;
                }

                string propertyValue = binding.Kind == ReferencePropertyBindingKind.File
                    ? sourceFile
                    : Path.GetDirectoryName(sourceFile)!;
                if (!overrides.TryGetValue(configuration.Name, out Dictionary<string, string>? configurationOverrides))
                {
                    configurationOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    overrides[configuration.Name] = configurationOverrides;
                }

                configurationOverrides[binding.PropertyName] = propertyValue;
            }
        }

        if (overrides.Count == 0)
        {
            return false;
        }

        string propsPath = Path.Combine(projectDirectory, $"{ExternalReferenceStagingDirectoryName}.props");
        WriteConfigurationReferenceOverrides(propsPath, overrides);
        EnsureGeneratedPropsImport(sandboxProjectPath, Path.GetFileName(propsPath));
        return true;
    }

    private static Dictionary<string, ReferencePropertyBinding> GetReferencePropertyBindings(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var bindings = new Dictionary<string, ReferencePropertyBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement reference in document.Descendants().Where(IsNamed("Reference")))
        {
            string include = reference.Attribute("Include")?.Value.Trim() ?? string.Empty;
            XElement? hintPathElement = reference.Elements().FirstOrDefault(IsNamed("HintPath"));
            if (string.IsNullOrWhiteSpace(include) || hintPathElement is null)
            {
                continue;
            }

            string hintPath = hintPathElement.Value.Trim();
            if (!TryGetReferencePropertyBinding(hintPath, out ReferencePropertyBinding binding))
            {
                continue;
            }

            bindings[include] = binding;
        }

        return bindings;
    }

    private static bool TryGetReferencePropertyBinding(string hintPath, out ReferencePropertyBinding binding)
    {
        binding = null!;
        Match match = Regex.Match(
            hintPath.Trim(),
            @"^\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\)(?<suffix>(?:[\\/][^<>:""|?*]+\.dll)?)$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        string propertyName = match.Groups["property"].Value;
        if (!IsModDependencyProperty(propertyName))
        {
            return false;
        }

        string suffix = match.Groups["suffix"].Value;
        ReferencePropertyBindingKind kind = string.IsNullOrWhiteSpace(suffix)
            ? ReferencePropertyBindingKind.File
            : ReferencePropertyBindingKind.Directory;
        string? fileName = string.IsNullOrWhiteSpace(suffix) ? null : Path.GetFileName(suffix);
        binding = new ReferencePropertyBinding(propertyName, kind, fileName);
        return true;
    }

    private static bool TryGetReferencePropertyBinding(
        string projectPath,
        ReferenceInfo reference,
        string configurationName,
        out ReferencePropertyBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(reference.HintPath) &&
            TryGetReferencePropertyBinding(reference.HintPath, out binding))
        {
            return true;
        }

        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement referenceElement in document.Descendants().Where(IsNamed("Reference")))
        {
            if (!string.Equals(referenceElement.Attribute("Include")?.Value, reference.Include, StringComparison.OrdinalIgnoreCase) ||
                !ElementAppliesToConfiguration(referenceElement, configurationName))
            {
                continue;
            }

            XElement? hintPath = referenceElement.Elements().FirstOrDefault(IsNamed("HintPath"));
            if (hintPath is not null &&
                TryGetReferencePropertyBinding(hintPath.Value, out binding))
            {
                return true;
            }
        }

        binding = null!;
        return false;
    }

    private static bool ElementAppliesToConfiguration(XElement element, string configurationName)
    {
        string condition = element.Attribute("Condition")?.Value
            ?? element.Parent?.Attribute("Condition")?.Value
            ?? string.Empty;
        return string.IsNullOrWhiteSpace(condition) ||
               condition.Contains(configurationName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExpectedReferenceFile(
        ReferenceInfo reference,
        string resolvedHintPath,
        ReferencePropertyBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.FileName))
        {
            return binding.FileName;
        }

        string resolvedFileName = Path.GetFileName(resolvedHintPath);
        if (resolvedFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return resolvedFileName;
        }

        string include = reference.Include.Split(',')[0].Trim();
        return include.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? include
            : $"{include}.dll";
    }

    private static bool ShouldStageWorkspaceReference(
        ReferenceInfo reference,
        string resolvedHintPath,
        ReferencePropertyBinding binding) =>
        IsModDependencyProperty(binding.PropertyName) ||
        GetMissingReferenceKind(reference, resolvedHintPath).Equals("ModDependencyMissing", StringComparison.OrdinalIgnoreCase);

    private static void WriteConfigurationReferenceOverrides(
        string propsPath,
        IReadOnlyDictionary<string, Dictionary<string, string>> overrides)
    {
        var document = new XDocument(
            new XElement("Project",
                overrides
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair =>
                        new XElement(
                            "PropertyGroup",
                            new XAttribute("Condition", $"'$(Configuration)'=='{pair.Key}'"),
                            pair.Value
                                .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(property => new XElement(property.Key, property.Value))))));
        document.Save(propsPath);
    }

    private static void EnsureGeneratedPropsImport(string projectPath, string propsFileName)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        XElement root = document.Root!;
        if (root.Elements()
            .Where(IsNamed("Import"))
            .Any(import => string.Equals(
                import.Attribute("Project")?.Value,
                propsFileName,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var importElement = new XElement(
            "Import",
            new XAttribute("Project", propsFileName),
            new XAttribute("Condition", $"Exists('{propsFileName}')"));

        XElement? lastImport = root.Elements().Where(IsNamed("Import")).LastOrDefault();
        if (lastImport is not null)
        {
            lastImport.AddAfterSelf(importElement);
        }
        else
        {
            root.AddFirst(importElement);
        }

        document.Save(projectPath);
    }

    private static bool StageProjectLocalReferenceFiles(
        string sandboxProjectPath,
        string sourceProjectDirectory,
        WorkspaceAnalysis analysis,
        MigrationVerifierOptions options)
    {
        ProjectAnalysis project = RequireSingleProject(analysis, sandboxProjectPath);
        string projectDirectory = Path.GetDirectoryName(sandboxProjectPath)!;
        string[] searchRoots = GetWorkspaceDependencySearchRoots(sourceProjectDirectory);
        bool copiedAny = false;
        foreach (ConfigurationAnalysis configuration in project.Configurations)
        {
            foreach (ReferenceInfo reference in configuration.References)
            {
                if (string.IsNullOrWhiteSpace(reference.HintPath))
                {
                    continue;
                }

                string resolvedHintPath = ResolveHintPath(projectDirectory, reference.HintPath, configuration.Runtime, options);
                if (File.Exists(resolvedHintPath) ||
                    !IsSandboxLocalPath(projectDirectory, resolvedHintPath) ||
                    !IsProjectLocalReferencePath(resolvedHintPath))
                {
                    continue;
                }

                string expectedFile = Path.GetFileName(resolvedHintPath);
                if (!expectedFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    !TryResolveReferenceSourceFile(reference, expectedFile, configuration.Runtime, options, searchRoots, out string? sourceFile) ||
                    sourceFile is null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(resolvedHintPath)!);
                File.Copy(sourceFile, resolvedHintPath, overwrite: true);
                copiedAny = true;
            }
        }

        return copiedAny;
    }

    private static bool TryResolveReferenceSourceFile(
        ReferenceInfo reference,
        string expectedFile,
        RuntimeKind runtime,
        MigrationVerifierOptions options,
        IReadOnlyList<string> searchRoots,
        out string? sourceFile)
    {
        sourceFile = null;
        foreach (string candidate in GetRuntimeReferenceSourceCandidates(reference, expectedFile, runtime, options))
        {
            if (File.Exists(candidate))
            {
                sourceFile = candidate;
                return true;
            }
        }

        return TryFindWorkspaceDependency(expectedFile, searchRoots, options, runtime, out sourceFile);
    }

    private static IEnumerable<string> GetRuntimeReferenceSourceCandidates(
        ReferenceInfo reference,
        string expectedFile,
        RuntimeKind runtime,
        MigrationVerifierOptions options)
    {
        string? gameRoot = GetGameRootForRuntime(runtime, options);
        if (gameRoot is null)
        {
            yield break;
        }

        string managedPath = runtime == RuntimeKind.Mono
            ? Path.Combine(gameRoot, "Schedule I_Data", "Managed")
            : Path.Combine(gameRoot, "MelonLoader", "Il2CppAssemblies");
        string melonLoaderPath = runtime == RuntimeKind.Mono
            ? Path.Combine(gameRoot, "MelonLoader", "net35")
            : Path.Combine(gameRoot, "MelonLoader", "net6");

        if (IsMelonLoaderReference(reference, expectedFile))
        {
            yield return Path.Combine(melonLoaderPath, expectedFile);
        }

        if (IsGameManagedReference(reference, expectedFile))
        {
            yield return Path.Combine(managedPath, expectedFile);
        }

        yield return Path.Combine(melonLoaderPath, expectedFile);
        yield return Path.Combine(managedPath, expectedFile);
        yield return Path.Combine(gameRoot, "Mods", expectedFile);
        yield return Path.Combine(gameRoot, "UserLibs", expectedFile);
        yield return Path.Combine(gameRoot, "Plugins", expectedFile);
    }

    private static bool IsMelonLoaderReference(ReferenceInfo reference, string expectedFile)
    {
        string text = $"{reference.Include}|{expectedFile}";
        return text.Contains("MelonLoader", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("0Harmony", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Harmony", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Il2CppInterop", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGameManagedReference(ReferenceInfo reference, string expectedFile)
    {
        string text = $"{reference.Include}|{expectedFile}";
        return text.Contains("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ScheduleOne", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Unity", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("FishNet", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("com.rlabrecque", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("EasyButtons", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("DOTween", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetExpectedIl2CppManagedReferenceFiles(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement hintPathElement in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("HintPath", StringComparison.OrdinalIgnoreCase)))
        {
            string hintPath = hintPathElement.Value.Trim();
            if (!hintPath.Contains("GameDllPath", StringComparison.OrdinalIgnoreCase) &&
                !hintPath.Contains("ManagedDllPath", StringComparison.OrdinalIgnoreCase) &&
                !hintPath.Contains("Il2CppManaged", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = Path.GetFileName(hintPath);
            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(fileName);
            }
        }

        return files.ToArray();
    }

    private static Dictionary<string, string[]> GetExpectedDependencyFilesByProperty(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var filesByProperty = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement hintPathElement in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("HintPath", StringComparison.OrdinalIgnoreCase)))
        {
            string hintPath = hintPathElement.Value.Trim();
            Match match = Regex.Match(
                hintPath,
                @"\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\)[\\/](?<file>[^\\/<>:""|?*]+\.dll)$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            string propertyName = match.Groups["property"].Value;
            if (!File.Exists(Path.Combine(projectDirectory, match.Groups["file"].Value)) &&
                !IsModDependencyProperty(propertyName))
            {
                continue;
            }

            if (!filesByProperty.TryGetValue(propertyName, out SortedSet<string>? files))
            {
                files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                filesByProperty[propertyName] = files;
            }

            files.Add(match.Groups["file"].Value);
        }

        return filesByProperty.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsModDependencyProperty(string propertyName) =>
        propertyName.Contains("Bgui", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("S1API", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("SteamNetworkLib", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("S1MAPI", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("MeshVault", StringComparison.OrdinalIgnoreCase);

    private static bool ExpectedFilesExist(string directory, IReadOnlyList<string> expectedFiles) =>
        !string.IsNullOrWhiteSpace(directory) &&
        expectedFiles.Count > 0 &&
        expectedFiles.All(file => File.Exists(Path.Combine(directory, file)));

    private static bool TryFindWorkspaceDependency(
        string expectedFile,
        IReadOnlyList<string> searchRoots,
        MigrationVerifierOptions options,
        RuntimeKind runtime,
        out string? sourceFile)
    {
        sourceFile = null;
        string[] candidateNames = GetWorkspaceDependencyCandidateNames(expectedFile);
        foreach (string candidateName in candidateNames)
        {
            string? bestCandidate = searchRoots
                .SelectMany(root => EnumerateWorkspaceDependencyCandidates(root, candidateName))
                .OrderByDescending(path => ScoreWorkspaceDependencyCandidate(path, expectedFile, options, runtime))
                .ThenBy(path => path.Length)
                .FirstOrDefault();
            if (bestCandidate is not null)
            {
                sourceFile = bestCandidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindWorkspaceDependency(
        string expectedFile,
        IReadOnlyList<string> searchRoots,
        MigrationVerifierOptions options,
        out string? sourceFile) =>
        TryFindWorkspaceDependency(expectedFile, searchRoots, options, RuntimeKind.Unknown, out sourceFile);

    private static string[] GetWorkspaceDependencyCandidateNames(string expectedFile)
    {
        string fileName = Path.GetFileName(expectedFile);
        return fileName.ToLowerInvariant() switch
        {
            "s1api.il2cpp.melonloader.dll" or "s1api.mono.melonloader.dll" => ["S1API.dll", fileName],
            "s1apiloader.melonloader.dll" => ["S1APILoader.dll", fileName],
            _ => [fileName]
        };
    }

    private static int ScoreWorkspaceDependencyCandidate(
        string path,
        string expectedFile,
        MigrationVerifierOptions options,
        RuntimeKind runtime = RuntimeKind.Unknown)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string expected = expectedFile.ToLowerInvariant();
        int score = 0;
        if (Path.GetFileName(path).Equals(expectedFile, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (IsWorkspaceDependencyAliasMatch(path, expectedFile))
        {
            score += 120;
        }

        if (expected.Contains("il2cpp", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.Contains($"{Path.DirectorySeparatorChar}Il2Cpp", StringComparison.OrdinalIgnoreCase))
            {
                score += 60;
            }

            if (!string.IsNullOrWhiteSpace(options.Il2CppGamePath) &&
                normalized.StartsWith(Path.GetFullPath(options.Il2CppGamePath), StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }

        if (expected.Contains("mono", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.Contains($"{Path.DirectorySeparatorChar}Mono", StringComparison.OrdinalIgnoreCase))
            {
                score += 60;
            }

            if (!string.IsNullOrWhiteSpace(options.MonoGamePath) &&
                normalized.StartsWith(Path.GetFullPath(options.MonoGamePath), StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }

        if (normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (runtime == RuntimeKind.Mono &&
            normalized.Contains("mono", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (runtime == RuntimeKind.Il2Cpp &&
            normalized.Contains("il2cpp", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            score -= 20;
        }

        return score;
    }

    private static bool IsWorkspaceDependencyAliasMatch(string path, string expectedFile)
    {
        string fileName = Path.GetFileName(path);
        return expectedFile.ToLowerInvariant() switch
        {
            "s1api.il2cpp.melonloader.dll" or "s1api.mono.melonloader.dll" =>
                fileName.Equals("S1API.dll", StringComparison.OrdinalIgnoreCase),
            "s1apiloader.melonloader.dll" =>
                fileName.Equals("S1APILoader.dll", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static IEnumerable<string> EnumerateWorkspaceDependencyCandidates(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }

            foreach (string child in EnumerateWorkspaceDependencyDirectories(directory))
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<string> EnumerateWorkspaceDependencyDirectories(string directory)
    {
        string[] children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception ex) when (IsTraversalException(ex))
        {
            yield break;
        }

        foreach (string child in children)
        {
            string name = Path.GetFileName(child);
            if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Il2CppAssemblies", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(ExternalReferenceStagingDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return child;
        }
    }

    private static bool IsTraversalException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DirectoryNotFoundException;

    private static string[] GetWorkspaceDependencySearchRoots(string sourceProjectDirectory)
    {
        var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string anchor in GetSearchAnchors(sourceProjectDirectory))
        {
            AddKnownWorkspaceDependencyRoots(anchor, roots);
        }

        return roots.ToArray();
    }

    private static IEnumerable<string> GetSearchAnchors(string sourceProjectDirectory)
    {
        foreach (string ancestor in EnumerateAncestors(sourceProjectDirectory).Take(5))
        {
            yield return ancestor;
        }

        foreach (string ancestor in EnumerateAncestors(Directory.GetCurrentDirectory()).Take(5))
        {
            yield return ancestor;
        }
    }

    private static IEnumerable<string> EnumerateAncestors(string start)
    {
        string? current = Path.GetFullPath(start);
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            current = Directory.GetParent(current)?.FullName;
        }
    }

    private static void AddKnownWorkspaceDependencyRoots(string anchor, ISet<string> roots)
    {
        if (!Directory.Exists(anchor))
        {
            return;
        }

        string[] knownNames =
        [
            "S1API",
            "SteamNetworkLib",
            "bGUI",
            "s1-employeetweaks",
            "S1MAPI",
            "MAPI",
            "MeshVault"
        ];

        foreach (string name in knownNames)
        {
            string candidate = Path.Combine(anchor, name);
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
            }
        }
    }

    private static MigrationBuildIssue[] CreateOutputIssues(string classifiedKind, string classifiedSummary, string output)
    {
        if (classifiedKind.Equals("None", StringComparison.Ordinal))
        {
            return Array.Empty<MigrationBuildIssue>();
        }

        string kind = classifiedKind;
        string? include = null;
        string? remediation = null;
        IReadOnlyList<string>? restoreSources = null;
        if (classifiedKind.Equals("MissingPackage", StringComparison.OrdinalIgnoreCase))
        {
            Match match = MissingPackageRegex.Match(classifiedSummary);
            if (match.Success)
            {
                include = match.Groups["id"].Value.Trim();
            }

            restoreSources = ExtractRestoreSources(output);
            kind = "PackageFeedMissing";
            remediation = include is null
                ? "Add the NuGet source that provides the missing package and run restore again."
                : $"Add the NuGet source that provides {include} and run restore again.";
        }
        else if (classifiedKind.Equals("ReferenceSurfaceMissing", StringComparison.OrdinalIgnoreCase))
        {
            kind = "ReferenceSurfaceMissing";
            remediation = "Check that the selected runtime game root and local dependency properties point at the assemblies that expose this namespace/type for the active configuration.";
        }
        else if (classifiedKind.Equals("MissingReference", StringComparison.OrdinalIgnoreCase))
        {
            Match match = MissingReferenceOutputRegex.Match(classifiedSummary);
            if (match.Success)
            {
                include = match.Groups["include"].Value.Trim();
            }

            remediation = "Fix the HintPath or imported local props so this reference resolves on this machine.";
        }
        else if (classifiedKind.Equals("Il2CppApiSurfaceMismatch", StringComparison.OrdinalIgnoreCase))
        {
            kind = "Il2CppApiSurfaceMismatch";
            remediation = "Add an IL2CPP-safe shim, facade alias, or runtime-specific source branch for this generated-wrapper API difference.";
        }

        return
        [
            new MigrationBuildIssue(
                kind,
                string.IsNullOrWhiteSpace(classifiedSummary)
                    ? FirstOutputLine(output) ?? "Build failed without a recognized error line."
                    : classifiedSummary,
                include,
                Remediation: remediation,
                RestoreSources: restoreSources)
        ];
    }

    private static IReadOnlyList<string>? ExtractRestoreSources(string output)
    {
        Match match = MissingPackageSourcesRegex.Match(output);
        if (!match.Success)
        {
            return null;
        }

        string[] sources = match.Groups["sources"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return sources.Length == 0 ? null : sources;
    }

    private static MigrationBuildIssue[] EnrichPackageIssues(
        ConfigurationAnalysis configuration,
        IReadOnlyList<MigrationBuildIssue> issues)
    {
        return issues
            .Select(issue =>
            {
                if (!issue.Kind.Equals("PackageFeedMissing", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(issue.Include))
                {
                    return issue;
                }

                PackageReferenceInfo? package = configuration.PackageReferences.FirstOrDefault(packageReference =>
                    string.Equals(packageReference.Include, issue.Include, StringComparison.OrdinalIgnoreCase));
                if (package is null)
                {
                    return issue;
                }

                string versionSuffix = string.IsNullOrWhiteSpace(package.Version)
                    ? string.Empty
                    : $" {package.Version}";
                return issue with
                {
                    Message = $"Package '{package.Include}{versionSuffix}' is not available from the configured NuGet sources for {configuration.Name}.",
                    SourcePath = package.SourcePath,
                    Imported = package.Imported,
                    Version = package.Version,
                    Remediation = issue.RestoreSources is { Count: > 0 }
                        ? $"Add the NuGet source that provides {package.Include}{versionSuffix} and run restore again. Current restore sources: {string.Join(", ", issue.RestoreSources)}."
                        : $"Add the NuGet source that provides {package.Include}{versionSuffix} and run restore again."
                };
            })
            .ToArray();
    }

    private static MigrationBuildIssue[] FilterOutputIssues(
        IReadOnlyList<MigrationBuildIssue> readinessIssues,
        IReadOnlyList<MigrationBuildIssue> outputIssues)
    {
        if (readinessIssues.Count == 0)
        {
            return outputIssues.ToArray();
        }

        return outputIssues
            .Where(issue =>
                !issue.Kind.Equals("MissingReference", StringComparison.OrdinalIgnoreCase) &&
                !issue.Kind.Equals("CompileError", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static (string ReadinessStatus, string Attribution) ClassifyBuildAttribution(
        bool success,
        bool timedOut,
        IReadOnlyList<MigrationBuildIssue> readinessIssues,
        IReadOnlyList<MigrationBuildIssue> outputIssues,
        string classifiedKind)
    {
        if (success)
        {
            return ("Ready", "None");
        }

        if (timedOut)
        {
            return ("Timeout", "ToolingFailure");
        }

        bool hasLocalReferenceIssues = readinessIssues.Any(IsLocalReferenceReadinessIssue) ||
                                       outputIssues.Any(IsLocalReferenceReadinessIssue);
        bool hasPackageIssues = outputIssues.Any(issue =>
            issue.Kind.Equals("PackageFeedMissing", StringComparison.OrdinalIgnoreCase));
        bool hasReferenceSurfaceIssues = outputIssues.Any(issue =>
            issue.Kind.Equals("ReferenceSurfaceMissing", StringComparison.OrdinalIgnoreCase));
        bool hasIl2CppApiSurfaceIssues = outputIssues.Any(issue =>
            issue.Kind.Equals("Il2CppApiSurfaceMismatch", StringComparison.OrdinalIgnoreCase));
        bool hasCompileErrors = outputIssues.Any(issue =>
            issue.Kind.Equals("CompileError", StringComparison.OrdinalIgnoreCase));

        if ((hasLocalReferenceIssues || hasPackageIssues || hasReferenceSurfaceIssues || hasIl2CppApiSurfaceIssues) && hasCompileErrors)
        {
            return ("Mixed", "Mixed");
        }

        if ((hasPackageIssues || hasReferenceSurfaceIssues) && hasLocalReferenceIssues)
        {
            return ("BlockedByDependencies", "DependencyNotReady");
        }

        if (hasReferenceSurfaceIssues)
        {
            return ("BlockedByReferenceSurface", "DependencyNotReady");
        }

        if (hasIl2CppApiSurfaceIssues)
        {
            return ("CompileFailed", "MigrationCompileFailure");
        }

        if (hasPackageIssues)
        {
            return ("BlockedByPackageRestore", "DependencyNotReady");
        }

        if (hasLocalReferenceIssues)
        {
            return ("BlockedByLocalReferences", "DependencyNotReady");
        }

        if (hasCompileErrors || classifiedKind.Equals("CompileError", StringComparison.OrdinalIgnoreCase))
        {
            return ("CompileFailed", "MigrationCompileFailure");
        }

        if (classifiedKind.Equals("RestoreError", StringComparison.OrdinalIgnoreCase))
        {
            return ("RestoreFailed", "ToolingFailure");
        }

        return ("UnknownFailure", "ToolingFailure");
    }

    private static string GetMissingReferenceKind(ReferenceInfo reference, string resolvedHintPath)
    {
        if (ContainsPathPart(resolvedHintPath, "MelonLoader", "Il2CppAssemblies") ||
            IsStagedIl2CppManagedReference(reference, resolvedHintPath))
        {
            return "GeneratedIl2CppAssembliesMissing";
        }

        if (ContainsPathPart(resolvedHintPath, "Schedule I_Data", "Managed"))
        {
            return "MonoManagedAssembliesMissing";
        }

        if (ContainsPathPart(resolvedHintPath, "MelonLoader", "net6") ||
            ContainsPathPart(resolvedHintPath, "MelonLoader", "net35"))
        {
            return "MelonLoaderRuntimeMissing";
        }

        if (ContainsPathPart(resolvedHintPath, "Mods") ||
            ContainsPathPart(resolvedHintPath, "UserLibs") ||
            ContainsPathPart(resolvedHintPath, "bin") ||
            ContainsPathPart(resolvedHintPath, "lib") ||
            ContainsPathPart(resolvedHintPath, "libs") ||
            ContainsPathPart(resolvedHintPath, "Libs") ||
            ContainsPathPart(resolvedHintPath, "build", "lib") ||
            ContainsPathPart(resolvedHintPath, "dependencies") ||
            ContainsPathPart(resolvedHintPath, "il2cpp_libs") ||
            ContainsPathPart(resolvedHintPath, "mono_libs") ||
            ContainsPathPart(resolvedHintPath, "packages") ||
            ContainsPathPart(resolvedHintPath, "Workspace", "lib") ||
            reference.Include.Contains("SteamNetworkLib", StringComparison.OrdinalIgnoreCase))
        {
            return "ModDependencyMissing";
        }

        return "MissingReference";
    }

    private static bool IsStagedIl2CppManagedReference(ReferenceInfo reference, string resolvedHintPath) =>
        ContainsPathPart(resolvedHintPath, ExternalReferenceStagingDirectoryName, "Il2CppManagedDllPath") &&
        (reference.Include.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
         Path.GetFileName(resolvedHintPath).StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase));

    private static string GetMissingReferenceMessage(
        string kind,
        ReferenceInfo reference,
        string configurationName,
        string resolvedHintPath) =>
        kind.Equals("LocalBuildPropertiesUnset", StringComparison.OrdinalIgnoreCase)
            ? $"Reference '{reference.Include}' resolved to a drive-root path for {configurationName}; local build path properties are likely unset: {resolvedHintPath}"
            : $"Reference '{reference.Include}' points to a missing local assembly for {configurationName}: {resolvedHintPath}";

    private static string GetMissingReferenceRemediation(string kind) =>
        kind switch
        {
            "GeneratedIl2CppAssembliesMissing" => "Set the IL2CPP game root to a local Schedule I install and launch it with MelonLoader once so MelonLoader/Il2CppAssemblies is generated.",
            "MonoManagedAssembliesMissing" => "Set the Mono game root to the alternate/Mono Schedule I install so Schedule I_Data/Managed references resolve.",
            "MelonLoaderRuntimeMissing" => "Set the game root to an install with the matching MelonLoader runtime assemblies for this backend.",
            "ModDependencyMissing" => "Restore or configure the sibling mod dependency path before using build verification.",
            "LocalBuildPropertiesUnset" => "Fill in the generated local.build.props path properties for this machine, or restore the imported local paths file that defines them.",
            _ => "Fix the HintPath or imported local props so this reference resolves on this machine."
        };

    private static MigrationBuildIssue[] CollapseReadinessIssues(
        ConfigurationAnalysis configuration,
        IReadOnlyList<MigrationBuildIssue> issues)
    {
        var collapsed = new List<MigrationBuildIssue>();
        var grouped = issues.GroupBy(issue => GetIssueGroupKey(issue), StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, MigrationBuildIssue> group in grouped)
        {
            MigrationBuildIssue first = group.First();
            int count = group.Count();
            if (count == 1 || !IsCollapsibleReadinessKind(first.Kind))
            {
                collapsed.AddRange(group);
                continue;
            }

            string? rootPath = GetReadinessRootPath(first);
            string target = rootPath is null ? "the configured dependency root" : rootPath;
            string sample = FormatMissingReferenceSample(group);
            collapsed.Add(first with
            {
                Include = $"{count} references",
                Path = rootPath,
                Message = first.Kind switch
                {
                    "GeneratedIl2CppAssembliesMissing" => $"Generated IL2CPP wrapper references are missing for {configuration.Name} under {target} ({count} references{sample}).",
                    "MonoManagedAssembliesMissing" => $"Mono managed assembly references are missing for {configuration.Name} under {target} ({count} references{sample}).",
                    "MelonLoaderRuntimeMissing" => $"MelonLoader runtime assembly references are missing for {configuration.Name} under {target} ({count} references{sample}).",
                    "LocalBuildPropertiesUnset" => $"Local build path properties are unset for {configuration.Name}; {count} references resolved to drive-root paths.",
                    _ => $"{count} local references are missing for {configuration.Name}: {target}{sample}."
                }
            });
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return collapsed
            .Where(issue => seen.Add($"{issue.Kind}|{issue.Include}|{issue.Path}"))
            .Take(MaxBuildIssues)
            .ToArray();
    }

    private static string FormatMissingReferenceSample(IEnumerable<MigrationBuildIssue> issues)
    {
        string[] includes = issues
            .Select(issue => issue.Include)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray()!;
        if (includes.Length == 0)
        {
            return string.Empty;
        }

        return $"; e.g. {string.Join(", ", includes)}";
    }

    private static string GetIssueGroupKey(MigrationBuildIssue issue)
    {
        if (issue.Kind.Equals("LocalBuildPropertiesUnset", StringComparison.OrdinalIgnoreCase))
        {
            return issue.Kind;
        }

        if (IsCollapsibleReadinessKind(issue.Kind) &&
            GetReadinessRootPath(issue) is string rootPath)
        {
            return $"{issue.Kind}|{rootPath}";
        }

        return $"{issue.Kind}|{issue.Include}|{issue.Path}";
    }

    private static bool IsCollapsibleReadinessKind(string kind) =>
        kind.Equals("GeneratedIl2CppAssembliesMissing", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("MonoManagedAssembliesMissing", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("MelonLoaderRuntimeMissing", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("LocalBuildPropertiesUnset", StringComparison.OrdinalIgnoreCase);

    private static string? GetReadinessRootPath(MigrationBuildIssue issue)
    {
        if (issue.Path is null)
        {
            return null;
        }

        string? marker = issue.Kind switch
        {
            "GeneratedIl2CppAssembliesMissing" => FindPathMarker(issue.Path, "MelonLoader", "Il2CppAssemblies") ??
                                                   FindPathMarker(issue.Path, ExternalReferenceStagingDirectoryName, "Il2CppManagedDllPath"),
            "MonoManagedAssembliesMissing" => FindPathMarker(issue.Path, "Schedule I_Data", "Managed"),
            "MelonLoaderRuntimeMissing" => FindPathMarker(issue.Path, "MelonLoader", "net6") ??
                                           FindPathMarker(issue.Path, "MelonLoader", "net35"),
            _ => null
        };

        return marker;
    }

    private static string ResolveHintPath(
        string projectDirectory,
        string hintPath,
        RuntimeKind runtime,
        MigrationVerifierOptions options)
    {
        string expanded = Environment.ExpandEnvironmentVariables(hintPath.Trim());
        if (IsRootRelativePath(expanded) &&
            GetGameRootForRuntime(runtime, options) is string gameRoot)
        {
            string suffix = expanded.TrimStart('\\', '/');
            return Path.GetFullPath(Path.Combine(gameRoot, suffix));
        }

        if (TryResolveAgainstRuntimeRoot(expanded, runtime, options, out string? runtimeRootPath))
        {
            return runtimeRootPath;
        }

        return Path.IsPathFullyQualified(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(projectDirectory, expanded));
    }

    private static bool TryResolveAgainstRuntimeRoot(
        string path,
        RuntimeKind runtime,
        MigrationVerifierOptions options,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        string? gameRoot = GetGameRootForRuntime(runtime, options);
        if (gameRoot is null || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string[] markerParts = runtime switch
        {
            RuntimeKind.Mono => [@"Schedule I_Data\Managed", @"MelonLoader\net35"],
            RuntimeKind.Il2Cpp => [@"MelonLoader\Il2CppAssemblies", @"MelonLoader\net6"],
            _ => []
        };

        foreach (string markerPart in markerParts)
        {
            int index = normalizedPath.IndexOf(markerPart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            string suffix = normalizedPath[index..].TrimStart('\\', '/');
            resolvedPath = Path.GetFullPath(Path.Combine(gameRoot, suffix));
            return true;
        }

        return false;
    }

    private static IEnumerable<string> GetBuildPropertyArguments(
        string projectPath,
        RuntimeKind runtime,
        MigrationVerifierOptions options)
    {
        foreach (KeyValuePair<string, string> property in GetLocalBuildProperties(projectPath))
        {
            yield return $"-p:{property.Key}={property.Value}";
        }

        foreach (KeyValuePair<string, string> property in GetBuildProperties(runtime, options))
        {
            yield return $"-p:{property.Key}={property.Value}";
        }

        foreach (KeyValuePair<string, string> property in GetWorkspaceDependencyBuildProperties(projectPath, runtime, options))
        {
            yield return $"-p:{property.Key}={property.Value}";
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> GetLocalBuildProperties(string projectPath)
    {
        string localPropsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "local.build.props");
        if (!File.Exists(localPropsPath))
        {
            yield break;
        }

        XDocument document = XDocument.Load(localPropsPath, LoadOptions.PreserveWhitespace);
        foreach (XElement property in document.Descendants().Where(element => !element.HasElements))
        {
            string name = property.Name.LocalName;
            string value = property.Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new KeyValuePair<string, string>(name, value);
        }
    }

    private static SortedDictionary<string, string> GetBuildProperties(RuntimeKind runtime, MigrationVerifierOptions options)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddExplicitGameRootProperties(properties, options);

        string? gameRoot = GetGameRootForRuntime(runtime, options);
        if (gameRoot is null)
        {
            return properties;
        }

        string managedPath = runtime == RuntimeKind.Mono
            ? Path.Combine(gameRoot, "Schedule I_Data", "Managed")
            : Path.Combine(gameRoot, "MelonLoader", "Il2CppAssemblies");
        string melonLoaderPath = runtime == RuntimeKind.Mono
            ? Path.Combine(gameRoot, "MelonLoader", "net35")
            : Path.Combine(gameRoot, "MelonLoader", "net6");

        properties["GameDir"] = gameRoot;
        properties["GamePath"] = gameRoot;
        properties["S1Dir"] = gameRoot;
        properties["ManagedPath"] = managedPath;
        properties["GameDllPath"] = managedPath;
        properties["MLPath"] = melonLoaderPath;
        properties["MelonLoaderPath"] = melonLoaderPath;
        properties["ModsPath"] = Path.Combine(gameRoot, "Mods");
        properties["ModOutputPath"] = Path.Combine(gameRoot, "Mods");

        if (runtime == RuntimeKind.Mono)
        {
            properties["MonoGamePath"] = gameRoot;
            properties["MonoManagedDllPath"] = managedPath;
            properties["MonoManagedPath"] = managedPath;
            properties["MonoMelonLoaderPath"] = melonLoaderPath;
            properties["MelonLoaderNet35Path"] = melonLoaderPath;
            properties["MonoModOutputPath"] = Path.Combine(gameRoot, "Mods");
            AddMonoModDependencyProperties(properties, gameRoot);
        }
        else if (runtime == RuntimeKind.Il2Cpp)
        {
            properties["Il2CppGamePath"] = gameRoot;
            properties["Il2CppAssembliesPath"] = managedPath;
            properties["Il2CppManagedDllPath"] = managedPath;
            properties["Il2CppManagedPath"] = managedPath;
            properties["ManagedDllPath"] = managedPath;
            properties["MelonLoaderNet6Path"] = melonLoaderPath;
            AddIl2CppModDependencyProperties(properties, gameRoot);
        }

        return properties;
    }

    private static SortedDictionary<string, string> GetWorkspaceDependencyBuildProperties(
        string projectPath,
        RuntimeKind runtime,
        MigrationVerifierOptions options)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (runtime is not (RuntimeKind.Mono or RuntimeKind.Il2Cpp))
        {
            return properties;
        }

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string[] searchRoots = GetWorkspaceDependencySearchRoots(projectDirectory);
        if (!TryFindWorkspaceDependency("SteamNetworkLib.dll", searchRoots, options, runtime, out string? steamNetworkLibPath) ||
            steamNetworkLibPath is null)
        {
            return properties;
        }

        if (runtime == RuntimeKind.Mono)
        {
            properties["SteamNetworkLibMonoAssemblyPath"] = steamNetworkLibPath;
        }
        else
        {
            properties["SteamNetworkLibIl2CppAssemblyPath"] = steamNetworkLibPath;
        }

        return properties;
    }

    private static void AddMonoModDependencyProperties(IDictionary<string, string> properties, string gameRoot)
    {
        string modsPath = Path.Combine(gameRoot, "Mods");
        string userLibsPath = Path.Combine(gameRoot, "UserLibs");
        properties["MonoS1APIPath"] = DirectoryContainsDll(modsPath, "S1API*.dll") ? modsPath : userLibsPath;
        properties["MonoSteamNetworkLibPath"] = DirectoryContainsDll(userLibsPath, "SteamNetworkLib*.dll") ? userLibsPath : modsPath;
        properties["MonoS1MAPIPath"] = DirectoryContainsDll(userLibsPath, "S1MAPI*.dll") ? userLibsPath : modsPath;
        properties["MonoMeshVaultPath"] = DirectoryContainsDll(userLibsPath, "MeshVault*.dll") ? userLibsPath : modsPath;
    }

    private static void AddIl2CppModDependencyProperties(IDictionary<string, string> properties, string gameRoot)
    {
        string modsPath = Path.Combine(gameRoot, "Mods");
        string pluginsPath = Path.Combine(gameRoot, "Plugins");
        string userLibsPath = Path.Combine(gameRoot, "UserLibs");
        properties["S1APIModsPath"] = modsPath;
        properties["S1APIPluginsPath"] = DirectoryContainsDll(pluginsPath, "S1APILoader*.dll") ? pluginsPath : modsPath;
        properties["SteamNetworkLibPath"] = DirectoryContainsDll(userLibsPath, "SteamNetworkLib*.dll") ? userLibsPath : modsPath;
        properties["S1MAPIPath"] = DirectoryContainsDll(userLibsPath, "S1MAPI*.dll") ? userLibsPath : modsPath;
        properties["MeshVaultPath"] = DirectoryContainsDll(userLibsPath, "MeshVault*.dll") ? userLibsPath : modsPath;
    }

    private static bool DirectoryContainsDll(string directory, string pattern) =>
        Directory.Exists(directory) &&
        Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any();

    private static void AddExplicitGameRootProperties(
        IDictionary<string, string> properties,
        MigrationVerifierOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Il2CppGamePath))
        {
            string root = Path.GetFullPath(options.Il2CppGamePath);
            properties["Il2CppGamePath"] = root;
            properties["Il2CppGameRoot"] = root;
            properties["Il2CppManagedDllPath"] = Path.Combine(root, "MelonLoader", "Il2CppAssemblies");
            properties["Il2CppManagedPath"] = Path.Combine(root, "MelonLoader", "Il2CppAssemblies");
            properties["ManagedDllPath"] = Path.Combine(root, "MelonLoader", "Il2CppAssemblies");
            properties["MelonLoaderNet6Path"] = Path.Combine(root, "MelonLoader", "net6");
            AddIl2CppModDependencyProperties(properties, root);
        }

        if (!string.IsNullOrWhiteSpace(options.MonoGamePath))
        {
            string root = Path.GetFullPath(options.MonoGamePath);
            properties["MonoGamePath"] = root;
            properties["MonoGameRoot"] = root;
            properties["MonoAssembliesPath"] = Path.Combine(root, "Schedule I_Data", "Managed");
            properties["MonoManagedDllPath"] = Path.Combine(root, "Schedule I_Data", "Managed");
            properties["MonoManagedPath"] = Path.Combine(root, "Schedule I_Data", "Managed");
            properties["MelonLoaderNet35Path"] = Path.Combine(root, "MelonLoader", "net35");
            properties["MonoMelonLoaderPath"] = Path.Combine(root, "MelonLoader", "net35");
            AddMonoModDependencyProperties(properties, root);
        }
    }

    private static string? GetKnownLocalPropertyValue(string propertyName, MigrationVerifierOptions options)
    {
        SortedDictionary<string, string> values = GetBuildProperties(RuntimeKind.Unknown, options);
        if (values.TryGetValue(propertyName, out string? exact))
        {
            return exact;
        }

        if (propertyName.Contains("Mono", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.MonoGamePath))
        {
            string root = Path.GetFullPath(options.MonoGamePath);
            if (propertyName.Contains("Managed", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("Assemblies", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(root, "Schedule I_Data", "Managed");
            }

            if (propertyName.Contains("Melon", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("ML", StringComparison.Ordinal))
            {
                return Path.Combine(root, "MelonLoader", "net35");
            }

            if (propertyName.Contains("Game", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("S1Dir", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }

        if ((propertyName.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
             propertyName.Contains("ManagedDll", StringComparison.OrdinalIgnoreCase) ||
             propertyName.Contains("MelonLoaderNet6", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(options.Il2CppGamePath))
        {
            string root = Path.GetFullPath(options.Il2CppGamePath);
            if (propertyName.Contains("Managed", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(root, "MelonLoader", "Il2CppAssemblies");
            }

            if (propertyName.Contains("Melon", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("ML", StringComparison.Ordinal))
            {
                return Path.Combine(root, "MelonLoader", "net6");
            }

            if (propertyName.Contains("Game", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }

        return null;
    }

    private static string? GetGameRootForRuntime(RuntimeKind runtime, MigrationVerifierOptions options)
    {
        string? path = runtime switch
        {
            RuntimeKind.Mono => options.MonoGamePath,
            RuntimeKind.Il2Cpp => options.Il2CppGamePath,
            _ => null
        };

        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    private static bool IsRootRelativePath(string value)
    {
        string text = value.Trim();
        return (text.StartsWith(@"\", StringComparison.Ordinal) && !text.StartsWith(@"\\", StringComparison.Ordinal)) ||
               (text.StartsWith("/", StringComparison.Ordinal) && !text.StartsWith("//", StringComparison.Ordinal));
    }

    private static bool IsLocalReferenceReadinessIssue(MigrationBuildIssue issue) =>
        issue.Kind.Equals("MissingReference", StringComparison.OrdinalIgnoreCase) ||
        issue.Kind.Equals("GeneratedIl2CppAssembliesMissing", StringComparison.OrdinalIgnoreCase) ||
        issue.Kind.Equals("MonoManagedAssembliesMissing", StringComparison.OrdinalIgnoreCase) ||
        issue.Kind.Equals("MelonLoaderRuntimeMissing", StringComparison.OrdinalIgnoreCase) ||
        issue.Kind.Equals("ModDependencyMissing", StringComparison.OrdinalIgnoreCase) ||
        issue.Kind.Equals("LocalBuildPropertiesUnset", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalModdingReference(string include)
    {
        string text = include.Trim();
        return text.Contains("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ScheduleOne", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Unity.", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("FishNet", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("MelonLoader", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Harmony", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPathPart(string path, params string[] parts) =>
        FindPathMarker(path, parts) is not null;

    private static bool IsSandboxLocalPath(string sandboxProjectDirectory, string path)
    {
        string fullSandboxDirectory = Path.GetFullPath(sandboxProjectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullSandboxDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectLocalReferencePath(string path)
    {
        string[] parts = Path.GetFullPath(path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            part.Equals("dependencies", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("il2cpp_libs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("lib", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("libs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("mono_libs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("packages", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("references", StringComparison.OrdinalIgnoreCase)) ||
            ContainsWorkspaceLibPath(parts);
    }

    private static bool ContainsWorkspaceLibPath(IReadOnlyList<string> parts)
    {
        for (int index = 0; index < parts.Count - 1; index++)
        {
            if (parts[index].Equals("Workspace", StringComparison.OrdinalIgnoreCase) &&
                parts[index + 1].Equals("lib", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindPathMarker(string path, params string[] parts)
    {
        if (parts.Length == 0)
        {
            return null;
        }

        string[] separators = [@"\", "/"];
        foreach (string separator in separators)
        {
            string marker = separator + string.Join(separator, parts) + separator;
            int index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return path[..(index + marker.Length - 1)];
            }
        }

        return null;
    }

    private static string? FirstOutputLine(string output) =>
        output
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static bool IsExternalReferenceSurfaceError(string line)
    {
        bool isMissingNamespaceOrType =
            line.Contains("error CS0234:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("error CS0246:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("error CS0012:", StringComparison.OrdinalIgnoreCase);
        return isMissingNamespaceOrType &&
               KnownExternalNamespaceHints.Any(hint => line.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExternalMemberSurfaceError(string line)
    {
        if (!line.Contains("error CS1061:", StringComparison.OrdinalIgnoreCase) ||
            !line.Contains("does not contain a definition for", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return KnownExternalMemberTypeHints.Any(hint => line.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static (string FailureKind, string Summary) ClassifyBuildResult(
        bool success,
        bool timedOut,
        string output,
        RuntimeKind runtime)
    {
        if (success)
        {
            return ("None", "Build target succeeded.");
        }

        if (timedOut)
        {
            return ("Timeout", "Build target exceeded the configured timeout.");
        }

        string[] lines = output
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? missingPackage = lines.FirstOrDefault(line =>
            line.Contains("error NU1101:", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("Unable to find package", StringComparison.OrdinalIgnoreCase));
        if (missingPackage is not null)
        {
            return ("MissingPackage", missingPackage);
        }

        string? restoreFailure = lines.FirstOrDefault(line =>
            line.Contains("error NETSDK1004:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("error NU", StringComparison.OrdinalIgnoreCase));
        if (restoreFailure is not null)
        {
            return ("RestoreError", restoreFailure);
        }

        string? unresolvedReference = lines.FirstOrDefault(line =>
            line.Contains("warning MSB3245:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Could not resolve this reference", StringComparison.OrdinalIgnoreCase));
        if (unresolvedReference is not null)
        {
            return ("MissingReference", unresolvedReference);
        }

        string? missingReferenceSurface = lines.FirstOrDefault(IsExternalReferenceSurfaceError);
        if (missingReferenceSurface is not null)
        {
            return ("ReferenceSurfaceMissing", missingReferenceSurface);
        }

        string? missingMemberSurface = lines.FirstOrDefault(IsExternalMemberSurfaceError);
        if (missingMemberSurface is not null)
        {
            if (runtime == RuntimeKind.Il2Cpp)
            {
                return ("Il2CppApiSurfaceMismatch", missingMemberSurface);
            }

            return ("ReferenceSurfaceMissing", missingMemberSurface);
        }

        string? compilerError = lines.FirstOrDefault(line =>
            line.Contains("error CS", StringComparison.OrdinalIgnoreCase));
        if (compilerError is not null)
        {
            return ("CompileError", compilerError);
        }

        string? firstError = lines.FirstOrDefault(line => line.Contains("error ", StringComparison.OrdinalIgnoreCase));
        return firstError is not null
            ? ("UnknownError", firstError)
            : ("UnknownError", "Build target failed without a recognized error line.");
    }

    private static void AppendBoundedLine(StringBuilder output, string line)
    {
        if (output.Length >= MaxBuildOutputChars)
        {
            return;
        }

        int remaining = MaxBuildOutputChars - output.Length;
        string text = line.Length + Environment.NewLine.Length > remaining
            ? line[..Math.Max(0, remaining - Environment.NewLine.Length)]
            : line;
        output.AppendLine(text);
    }

    private static string QuoteArgument(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;

    private static ProjectAnalysis RequireSingleProject(WorkspaceAnalysis analysis, string path)
    {
        if (analysis.Projects.Count == 1)
        {
            return analysis.Projects[0];
        }

        throw new InvalidOperationException(
            $"verify-migration requires exactly one project for now; found {analysis.Projects.Count} project(s) under {Path.GetFullPath(path)}.");
    }

    private static string CreateSandboxRoot()
    {
        string sandboxRoot = Path.Combine(Path.GetTempPath(), $"{SandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxRoot);
        return sandboxRoot;
    }

    private static void DeleteSandboxBuildOutputs(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        if (!Path.GetFileName(projectDirectory).StartsWith(SandboxPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeleteSandboxChildDirectory(projectDirectory, "bin");
        DeleteSandboxChildDirectory(projectDirectory, "obj");
    }

    private static void DeleteSandboxChildDirectory(string sandboxRoot, string childDirectoryName)
    {
        string target = Path.GetFullPath(Path.Combine(sandboxRoot, childDirectoryName));
        if (!target.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(target))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (string directory in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        File.SetAttributes(target, FileAttributes.Normal);
        Directory.Delete(target, recursive: true);
    }

    private static string FindSandboxSourceRoot(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        string? bestStructuredRoot = null;
        foreach (string ancestor in EnumerateAncestors(projectDirectory).Take(6))
        {
            if (Directory.Exists(Path.Combine(ancestor, ".git")))
            {
                return ancestor;
            }

            bool hasRepoMarker =
                File.Exists(Path.Combine(ancestor, ".gitignore")) ||
                File.Exists(Path.Combine(ancestor, "README.md")) ||
                File.Exists(Path.Combine(ancestor, "readme.md"));
            bool hasSourceLayout =
                Directory.Exists(Path.Combine(ancestor, "src")) &&
                Path.GetRelativePath(Path.Combine(ancestor, "src"), projectPath).StartsWith("..", StringComparison.Ordinal) is false;
            bool hasProjectAssets = Directory.Exists(Path.Combine(ancestor, "assets"));
            if ((hasRepoMarker || hasProjectAssets) && hasSourceLayout)
            {
                bestStructuredRoot = ancestor;
            }
        }

        return bestStructuredRoot ?? projectDirectory;
    }

    private static void CopyProjectDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var pending = new Stack<(string Source, string Target)>();
        pending.Push((sourceDirectory, targetDirectory));

        while (pending.Count > 0)
        {
            (string source, string target) = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(source))
            {
                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                string childTarget = Path.Combine(target, Path.GetFileName(directory));
                Directory.CreateDirectory(childTarget);
                pending.Push((directory, childTarget));
            }

            foreach (string file in Directory.EnumerateFiles(source))
            {
                if (ShouldSkipFile(file))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
        }
    }

    private static void StageAncestorNuGetConfig(string sourceProjectPath, string sourceRoot, string sandboxRoot)
    {
        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        if (File.Exists(Path.Combine(sandboxRoot, "NuGet.config")) ||
            File.Exists(Path.Combine(sandboxRoot, "nuget.config")))
        {
            return;
        }

        string sourceProjectDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceProjectPath))!;
        foreach (string ancestor in EnumerateAncestors(sourceProjectDirectory).Skip(1).Take(8))
        {
            string fullAncestor = Path.GetFullPath(ancestor);
            if (fullAncestor.StartsWith(fullSourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? configPath = FindNuGetConfig(fullAncestor);
            if (configPath is null)
            {
                continue;
            }

            File.Copy(configPath, Path.Combine(sandboxRoot, Path.GetFileName(configPath)), overwrite: false);
            return;
        }
    }

    private static string? FindNuGetConfig(string directory)
    {
        string configPath = Path.Combine(directory, "NuGet.config");
        if (File.Exists(configPath))
        {
            return configPath;
        }

        configPath = Path.Combine(directory, "nuget.config");
        return File.Exists(configPath) ? configPath : null;
    }

    private static bool ShouldSkipDirectory(string directory)
    {
        string directoryName = Path.GetFileName(directory);
        if (directoryName.Equals("S1Interop.Generated", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExcludedDirectoryNames.Contains(directoryName))
        {
            return true;
        }

        if (IsReservedWindowsDevicePath(directory))
        {
            return true;
        }

        return !TryGetAttributes(directory, out FileAttributes attributes) ||
            attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool ShouldSkipFile(string file)
    {
        if (IsReservedWindowsDevicePath(file))
        {
            return true;
        }

        if (!TryGetAttributes(file, out FileAttributes attributes) ||
            attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        string extension = Path.GetExtension(file);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
            IsProjectLocalDependencyFile(file))
        {
            return false;
        }

        return ExcludedFileExtensions.Contains(extension);
    }

    private static bool IsProjectLocalDependencyFile(string file)
    {
        string[] parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            part.Equals("build", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("dependencies", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("il2cpp_libs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("lib", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("libs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("mono_libs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("packages", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("references", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsReservedWindowsDevicePath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        string fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string deviceName = Path.GetFileNameWithoutExtension(fileName);
        return WindowsReservedDeviceNames.Contains(deviceName);
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            attributes = default;
            return false;
        }
    }

    private static string SanitizePathPart(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "Configuration" : builder.ToString();
    }

    private static Func<XElement, bool> IsNamed(string localName) =>
        element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private enum ReferencePropertyBindingKind
    {
        Directory,
        File
    }

    private sealed record ReferencePropertyBinding(
        string PropertyName,
        ReferencePropertyBindingKind Kind,
        string? FileName);

    private static bool TryDeleteSandbox(string sandboxRoot)
    {
        string fullSandboxRoot = Path.GetFullPath(sandboxRoot);
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullSandboxRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullSandboxRoot).StartsWith(SandboxPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Directory.Exists(fullSandboxRoot))
        {
            return true;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(fullSandboxRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (string directory in Directory.EnumerateDirectories(fullSandboxRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directory, FileAttributes.Normal);
            }

            File.SetAttributes(fullSandboxRoot, FileAttributes.Normal);
            Directory.Delete(fullSandboxRoot, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
