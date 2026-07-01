using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core;

public sealed class CsprojAnalyzer
{
    private static readonly Regex SimpleConfigurationConditionRegex = new(
        @"\$\(\s*Configuration\s*\)\s*'?\s*={1,2}\s*(?:'(?<name>[^'|]+)(?:\|[^']*)?'|(?<name>[^'""\)\s|]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QuotedComparisonRegex = new(
        @"'(?<left>[^']*)'\s*(?<operator>==|!=)\s*'(?<right>[^']*)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PreprocessorConditionRegex = new(
        @"^\s*#\s*(?:if|elif)\s+(?<condition>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex PreprocessorSymbolRegex = new(
        @"(?<![A-Za-z0-9_])(?<symbol>[A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    private static readonly Regex MsBuildIsOsPlatformRegex = new(
        @"^\s*\$\(\s*\[MSBuild\]::IsOSPlatform\(\s*['""](?<platform>[^'""]+)['""]\s*\)\s*\)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] CanonicalRuntimeSymbols = ["MONO", "IL2CPP"];

    public ProjectAnalysis AnalyzeProject(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        XDocument document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        IReadOnlyList<MsBuildElement> projectElements = ExpandProjectElements(fullPath);
        SourceConditionalSymbols sourceSymbols = DiscoverSourceConditionalSymbols(fullPath);
        SourceInteropAnalysis sourceInterop = new SourceInteropAnalyzer().Analyze(fullPath);

        IReadOnlyList<string> configurationNames = GetConfigurationNames(projectElements);
        var configurations = configurationNames
            .Select(name => AnalyzeConfiguration(projectElements, document, name))
            .ToArray();

        var diagnostics = new List<InteropDiagnostic>();
        foreach (ConfigurationAnalysis configuration in configurations)
        {
            AddConfigurationDiagnostics(fullPath, configuration, diagnostics);
            AddRuntimeDefineDiagnostics(fullPath, configuration, sourceSymbols, diagnostics);
        }

        AddProjectDiagnostics(fullPath, document, projectElements, diagnostics);
        diagnostics.AddRange(sourceInterop.Diagnostics);
        return new ProjectAnalysis(fullPath, configurations, diagnostics, sourceInterop);
    }

    private static IReadOnlyList<string> GetConfigurationNames(IReadOnlyList<MsBuildElement> projectElements)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement element in projectElements.Select(entry => entry.Element).Where(IsNamed("Configurations")))
        {
            foreach (string part in SplitMsBuildList(element.Value))
            {
                names.Add(part);
            }
        }

        foreach (XAttribute condition in projectElements.Select(entry => entry.Element).Attributes("Condition"))
        {
            foreach (string configurationName in ExtractConfigurationNames(condition.Value))
            {
                names.Add(configurationName);
            }
        }

        if (names.Count == 0)
        {
            names.Add("Debug");
            names.Add("Release");
        }

        return names.ToArray();
    }

    private static ConfigurationAnalysis AnalyzeConfiguration(
        IReadOnlyList<MsBuildElement> projectElements,
        XDocument document,
        string configurationName)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configurationName
        };

        foreach (XElement propertyGroup in projectElements.Select(entry => entry.Element).Where(IsNamed("PropertyGroup")))
        {
            string? condition = propertyGroup.Attribute("Condition")?.Value;
            if (!ConditionApplies(condition, configurationName, properties))
            {
                continue;
            }

            foreach (XElement property in propertyGroup.Elements())
            {
                string? propertyCondition = property.Attribute("Condition")?.Value;
                if (!ConditionApplies(propertyCondition, configurationName, properties))
                {
                    continue;
                }

                properties[property.Name.LocalName] = ExpandProperties(property.Value.Trim(), properties);
            }
        }

        string? targetFramework = FirstNonEmpty(
            properties.GetValueOrDefault("TargetFramework"),
            SplitMsBuildList(properties.GetValueOrDefault("TargetFrameworks")).FirstOrDefault());

        IReadOnlyList<string> defines = SplitMsBuildList(properties.GetValueOrDefault("DefineConstants"))
            .Where(value => !value.Contains("$(", StringComparison.Ordinal))
            .ToArray();

        IReadOnlyList<ReferenceInfo> references = GetReferences(projectElements, configurationName, properties);
        IReadOnlyList<PackageReferenceInfo> packageReferences = GetPackageReferences(projectElements, configurationName, properties);
        RuntimeScores scores = ScoreRuntime(configurationName, targetFramework, defines, references, packageReferences);
        RuntimeKind runtime = ChooseRuntime(scores);

        return new ConfigurationAnalysis(
            configurationName,
            runtime,
            scores.Mono,
            scores.Il2Cpp,
            scores.CrossCompat,
            targetFramework,
            defines,
            references,
            packageReferences,
            scores.Evidence);
    }

    private static IReadOnlyList<ReferenceInfo> GetReferences(
        IReadOnlyList<MsBuildElement> projectElements,
        string configurationName,
        IReadOnlyDictionary<string, string> properties)
    {
        var references = new List<ReferenceInfo>();

        foreach (MsBuildElement itemGroupEntry in projectElements.Where(entry => IsNamed("ItemGroup")(entry.Element)))
        {
            XElement itemGroup = itemGroupEntry.Element;
            string? itemGroupCondition = itemGroup.Attribute("Condition")?.Value;
            if (!ConditionApplies(itemGroupCondition, configurationName, properties))
            {
                continue;
            }

            foreach (XElement reference in itemGroup.Elements().Where(IsNamed("Reference")))
            {
                string? referenceCondition = reference.Attribute("Condition")?.Value;
                if (!ConditionApplies(referenceCondition, configurationName, properties))
                {
                    continue;
                }

                string include = reference.Attribute("Include")?.Value.Trim() ?? string.Empty;
                string? hintPath = reference.Elements().FirstOrDefault(IsNamed("HintPath"))?.Value.Trim();
                if (hintPath is not null)
                {
                    hintPath = ExpandProperties(hintPath, properties);
                }
                bool privateFalse = reference.Elements()
                    .Where(IsNamed("Private"))
                    .Any(element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

                references.Add(new ReferenceInfo(
                    include,
                    hintPath,
                    CombineConditions(itemGroupCondition, referenceCondition),
                    privateFalse,
                    itemGroupEntry.SourcePath,
                    itemGroupEntry.Imported));
            }
        }

        return references;
    }

    private static IReadOnlyList<PackageReferenceInfo> GetPackageReferences(
        IReadOnlyList<MsBuildElement> projectElements,
        string configurationName,
        IReadOnlyDictionary<string, string> properties)
    {
        var packages = new List<PackageReferenceInfo>();

        foreach (MsBuildElement itemGroupEntry in projectElements.Where(entry => IsNamed("ItemGroup")(entry.Element)))
        {
            XElement itemGroup = itemGroupEntry.Element;
            string? itemGroupCondition = itemGroup.Attribute("Condition")?.Value;
            if (!ConditionApplies(itemGroupCondition, configurationName, properties))
            {
                continue;
            }

            foreach (XElement packageReference in itemGroup.Elements().Where(IsNamed("PackageReference")))
            {
                string? packageCondition = packageReference.Attribute("Condition")?.Value;
                if (!ConditionApplies(packageCondition, configurationName, properties))
                {
                    continue;
                }

                string include = packageReference.Attribute("Include")?.Value.Trim() ?? string.Empty;
                string? version = FirstNonEmpty(
                    packageReference.Attribute("Version")?.Value.Trim(),
                    packageReference.Elements().FirstOrDefault(IsNamed("Version"))?.Value.Trim());
                if (version is not null)
                {
                    version = ExpandProperties(version, properties);
                }

                packages.Add(new PackageReferenceInfo(
                    include,
                    version,
                    CombineConditions(itemGroupCondition, packageCondition),
                    itemGroupEntry.SourcePath,
                    itemGroupEntry.Imported));
            }
        }

        return packages;
    }

    private static RuntimeScores ScoreRuntime(
        string configurationName,
        string? targetFramework,
        IReadOnlyList<string> defines,
        IReadOnlyList<ReferenceInfo> references,
        IReadOnlyList<PackageReferenceInfo> packageReferences)
    {
        var evidence = new List<string>();
        int mono = 0;
        int il2Cpp = 0;
        int crossCompat = 0;

        RuntimeNameIntent nameIntent = GetRuntimeNameIntent(configurationName);
        if (nameIntent == RuntimeNameIntent.Mono)
        {
            mono += 8;
            evidence.Add("configuration name contains Mono");
        }

        if (nameIntent == RuntimeNameIntent.Il2Cpp)
        {
            il2Cpp += 8;
            evidence.Add("configuration name contains IL2CPP/Cpp");
        }

        if (configurationName.Contains("cross", StringComparison.OrdinalIgnoreCase))
        {
            crossCompat += 3;
            evidence.Add("configuration name contains CrossCompat");
        }

        foreach (string define in defines)
        {
            if (define.Contains("MONO", StringComparison.OrdinalIgnoreCase))
            {
                mono += 2;
                evidence.Add($"define {define}");
            }

            if (define.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                il2Cpp += 2;
                evidence.Add($"define {define}");
            }

            if (define.Contains("CROSS", StringComparison.OrdinalIgnoreCase))
            {
                crossCompat += 2;
                evidence.Add($"define {define}");
            }
        }

        if (string.Equals(targetFramework, "netstandard2.1", StringComparison.OrdinalIgnoreCase))
        {
            mono += 1;
            crossCompat += 1;
            evidence.Add("TargetFramework netstandard2.1");
        }

        if (string.Equals(targetFramework, "net6.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetFramework, "net6", StringComparison.OrdinalIgnoreCase))
        {
            il2Cpp += 1;
            evidence.Add("TargetFramework net6.0/net6");
        }

        foreach (ReferenceInfo reference in references)
        {
            string combined = $"{reference.Include}|{reference.HintPath}";
            if (combined.Contains(@"Schedule I_Data\Managed", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains(@"/Schedule I_Data/Managed", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains(@"MelonLoader\net35", StringComparison.OrdinalIgnoreCase))
            {
                mono += 2;
                evidence.Add($"Mono reference {reference.Include}");
            }

            if (combined.Contains(@"MelonLoader\Il2CppAssemblies", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains(@"/MelonLoader/Il2CppAssemblies", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains(@"MelonLoader\net6", StringComparison.OrdinalIgnoreCase) ||
                reference.Include.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase))
            {
                il2Cpp += 2;
                evidence.Add($"IL2CPP reference {reference.Include}");
            }
        }

        foreach (PackageReferenceInfo packageReference in packageReferences)
        {
            string include = packageReference.Include;
            if (include.Contains("RefGen.Schedule-I.Mono", StringComparison.OrdinalIgnoreCase))
            {
                mono += 3;
                evidence.Add($"Mono package {include}");
            }

            if (include.Contains("RefGen.Schedule-I.Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("Il2CppInterop", StringComparison.OrdinalIgnoreCase))
            {
                il2Cpp += 3;
                evidence.Add($"IL2CPP package {include}");
            }
        }

        return new RuntimeScores(mono, il2Cpp, crossCompat, evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static RuntimeNameIntent GetRuntimeNameIntent(string configurationName)
    {
        bool containsMono = configurationName.Contains("mono", StringComparison.OrdinalIgnoreCase);
        bool containsIl2Cpp = configurationName.Contains("il2cpp", StringComparison.OrdinalIgnoreCase);

        if (containsMono == containsIl2Cpp)
        {
            return RuntimeNameIntent.None;
        }

        return containsMono ? RuntimeNameIntent.Mono : RuntimeNameIntent.Il2Cpp;
    }

    private static RuntimeKind ChooseRuntime(RuntimeScores scores)
    {
        if (scores.CrossCompat >= 5)
        {
            return RuntimeKind.CrossCompat;
        }

        if (scores.CrossCompat >= scores.Mono && scores.CrossCompat >= scores.Il2Cpp && scores.CrossCompat > 0)
        {
            return RuntimeKind.CrossCompat;
        }

        if (scores.Il2Cpp > scores.Mono)
        {
            return RuntimeKind.Il2Cpp;
        }

        if (scores.Mono > scores.Il2Cpp)
        {
            return RuntimeKind.Mono;
        }

        return RuntimeKind.Unknown;
    }

    private static void AddConfigurationDiagnostics(
        string projectPath,
        ConfigurationAnalysis configuration,
        List<InteropDiagnostic> diagnostics)
    {
        if (configuration.Runtime == RuntimeKind.Il2Cpp &&
            !IsTargetFramework(configuration.TargetFramework, "net6.0", "net6"))
        {
            diagnostics.Add(new InteropDiagnostic(
                "wrong_target_framework",
                DiagnosticSeverity.Error,
                "IL2CPP configurations should target net6.0 so they match MelonLoader CoreCLR and Il2CppInterop.",
                projectPath,
                configuration.Name,
                $"TargetFramework={configuration.TargetFramework ?? "<missing>"}"));
        }

        if (configuration.Runtime == RuntimeKind.Mono &&
            configuration.TargetFramework is not null &&
            !IsTargetFramework(configuration.TargetFramework, "netstandard2.1"))
        {
            diagnostics.Add(new InteropDiagnostic(
                "wrong_target_framework",
                DiagnosticSeverity.Warning,
                "Mono Schedule One MelonLoader configurations normally target netstandard2.1.",
                projectPath,
                configuration.Name,
                $"TargetFramework={configuration.TargetFramework}"));
        }

        var rootRelativeReferences = new List<ReferenceInfo>();
        foreach (ReferenceInfo reference in configuration.References)
        {
            if (configuration.Runtime == RuntimeKind.Il2Cpp &&
                reference.HintPath is not null &&
                reference.HintPath.Contains(@"Schedule I_Data\Managed", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new InteropDiagnostic(
                    "wrong_il2cpp_reference_surface",
                    DiagnosticSeverity.Error,
                    "IL2CPP configurations should build against MelonLoader-generated Il2CppAssemblies, not the Mono Managed folder.",
                    projectPath,
                    configuration.Name,
                    FormatReferenceEvidence(reference)));
            }

            if (reference.HintPath is not null &&
                reference.HintPath.Contains("Assembly-CSharp-publicized", StringComparison.OrdinalIgnoreCase))
            {
                if (configuration.Runtime != RuntimeKind.Mono || !reference.Imported)
                {
                    diagnostics.Add(new InteropDiagnostic(
                        "stale_publicized_surface",
                        configuration.Runtime == RuntimeKind.Mono ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                        "Publicized Assembly-CSharp copies are not a trustworthy IL2CPP source surface; use generated wrappers or build-time publicization for Mono.",
                        projectPath,
                        configuration.Name,
                        FormatReferenceEvidence(reference)));
                }
            }

            if (!reference.Imported && IsGameReference(reference) && !reference.PrivateFalse)
            {
                diagnostics.Add(new InteropDiagnostic(
                    "reference_should_not_copy_local",
                    DiagnosticSeverity.Warning,
                    "Game and Unity reference assemblies should normally set Private=false so they are not copied into mod output.",
                    projectPath,
                    configuration.Name,
                    reference.Include));
            }

            if (IsGameReference(reference) && IsRootRelativeWindowsPath(reference.HintPath))
            {
                rootRelativeReferences.Add(reference);
            }
        }

        if (rootRelativeReferences.Count > 0 && !HasLocalReferencePropertyScaffold(projectPath))
        {
            ReferenceInfo example = rootRelativeReferences[0];
            diagnostics.Add(new InteropDiagnostic(
                "missing_local_reference_properties",
                DiagnosticSeverity.Warning,
                "Reference HintPaths resolved to drive-root paths, usually because local MSBuild path properties are unset. Generate ignored local props scaffolding before build verification.",
                projectPath,
                configuration.Name,
                $"{rootRelativeReferences.Count} reference(s), example {FormatReferenceEvidence(example)}"));
        }

        bool hasIl2CppInterop = configuration.References.Any(reference =>
            reference.Include.Contains("Il2CppInterop.Runtime", StringComparison.OrdinalIgnoreCase));
        bool hasGeneratedIl2CppReference = configuration.References.Any(reference =>
            (reference.HintPath ?? string.Empty).Contains(@"MelonLoader\Il2CppAssemblies", StringComparison.OrdinalIgnoreCase));

        if (configuration.Runtime == RuntimeKind.Il2Cpp && hasGeneratedIl2CppReference && !hasIl2CppInterop)
        {
            diagnostics.Add(new InteropDiagnostic(
                "missing_il2cppinterop_reference",
                DiagnosticSeverity.Warning,
                "IL2CPP configs that directly use generated wrappers usually need Il2CppInterop.Runtime.",
                projectPath,
                configuration.Name,
                null));
        }
    }

    private static void AddProjectDiagnostics(
        string projectPath,
        XDocument document,
        IReadOnlyList<MsBuildElement> projectElements,
        List<InteropDiagnostic> diagnostics)
    {
        var seenEvidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement element in document.Descendants().Where(element => !element.HasElements))
        {
            string text = element.Value.Trim();
            if (IsAbsoluteWindowsPath(text) && seenEvidence.Add(text))
            {
                diagnostics.Add(new InteropDiagnostic(
                    "local_path_in_project",
                    DiagnosticSeverity.Warning,
                    "Local game paths should live in ignored local props or command-line properties, not committed project defaults.",
                    projectPath,
                    null,
                    text));
            }
        }

        if (new SdkFacadeGenerator().Plan(new ProjectAnalysis(
                projectPath,
                Array.Empty<ConfigurationAnalysis>(),
                Array.Empty<InteropDiagnostic>())).HasContent &&
            !SupportsGlobalUsings(projectElements))
        {
            string langVersion = GetProjectLanguageVersion(projectElements) ?? "<missing>";
            diagnostics.Add(new InteropDiagnostic(
                "global_usings_require_langversion",
                DiagnosticSeverity.Warning,
                "Generated S1Interop global using facades require C# 10.0 or newer.",
                projectPath,
                null,
                $"LangVersion={langVersion}"));
        }
    }

    private static bool SupportsGlobalUsings(IReadOnlyList<MsBuildElement> projectElements)
    {
        string? langVersion = GetProjectLanguageVersion(projectElements);
        if (langVersion is not null)
        {
            return IsCSharp10OrNewer(langVersion);
        }

        string[] targetFrameworks = projectElements
            .Select(entry => entry.Element)
            .Where(IsNamed("TargetFramework"))
            .Select(element => element.Value.Trim())
            .Concat(projectElements
                .Select(entry => entry.Element)
                .Where(IsNamed("TargetFrameworks"))
                .SelectMany(element => SplitMsBuildList(element.Value)))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return targetFrameworks.Length > 0 &&
               targetFrameworks.All(TargetFrameworkDefaultsToCSharp10OrNewer);
    }

    private static string? GetProjectLanguageVersion(IReadOnlyList<MsBuildElement> projectElements)
    {
        return projectElements
            .Select(entry => entry.Element)
            .Where(IsNamed("LangVersion"))
            .Select(element => element.Value.Trim())
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool IsCSharp10OrNewer(string langVersion)
    {
        string normalized = langVersion.Trim();
        if (normalized.Equals("latest", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Version.TryParse(normalized, out Version? version) && version.Major >= 10;
    }

    private static bool TargetFrameworkDefaultsToCSharp10OrNewer(string targetFramework)
    {
        Match match = Regex.Match(targetFramework, @"^net(?<major>\d+)(?:\.\d+)?$", RegexOptions.IgnoreCase);
        return match.Success &&
               int.TryParse(match.Groups["major"].Value, out int major) &&
               major >= 6;
    }

    private static void AddRuntimeDefineDiagnostics(
        string projectPath,
        ConfigurationAnalysis configuration,
        SourceConditionalSymbols sourceSymbols,
        List<InteropDiagnostic> diagnostics)
    {
        string? requiredSymbol = configuration.Runtime switch
        {
            RuntimeKind.Mono => "MONO",
            RuntimeKind.Il2Cpp => "IL2CPP",
            _ => null
        };

        if (requiredSymbol is null ||
            !sourceSymbols.Contains(requiredSymbol) ||
            configuration.DefineConstants.Any(define => string.Equals(define, requiredSymbol, StringComparison.Ordinal)))
        {
            return;
        }

        string defines = configuration.DefineConstants.Count == 0
            ? "<none>"
            : string.Join(";", configuration.DefineConstants);
        diagnostics.Add(new InteropDiagnostic(
            "missing_runtime_define",
            DiagnosticSeverity.Error,
            $"{configuration.Runtime} configuration should define {requiredSymbol} because source files use that conditional compilation symbol.",
            projectPath,
            configuration.Name,
            $"{sourceSymbols.GetEvidence(requiredSymbol)}; DefineConstants={defines}"));
    }

    private static SourceConditionalSymbols DiscoverSourceConditionalSymbols(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return SourceConditionalSymbols.Empty;
        }

        var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs"))
        {
            if (IsGeneratedOrBuildOutput(projectDirectory, file))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                Match conditionMatch = PreprocessorConditionRegex.Match(lines[index]);
                if (!conditionMatch.Success)
                {
                    continue;
                }

                foreach (Match symbolMatch in PreprocessorSymbolRegex.Matches(conditionMatch.Groups["condition"].Value))
                {
                    string symbol = symbolMatch.Groups["symbol"].Value;
                    if (!CanonicalRuntimeSymbols.Contains(symbol, StringComparer.Ordinal) ||
                        symbols.ContainsKey(symbol))
                    {
                        continue;
                    }

                    string relativePath = Path.GetRelativePath(projectDirectory, file);
                    symbols[symbol] = $"{relativePath}:{index + 1} uses {symbol}";
                }
            }
        }

        return new SourceConditionalSymbols(symbols);
    }

    private static bool IsGeneratedOrBuildOutput(string projectDirectory, string file)
    {
        return WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file);
    }

    private static bool IsGameReference(ReferenceInfo reference)
    {
        string text = $"{reference.Include}|{reference.HintPath}";
        return text.Contains("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ScheduleOne", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("FishNet", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("MelonLoader", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("0Harmony", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRootRelativeWindowsPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        return (text.StartsWith(@"\", StringComparison.Ordinal) && !text.StartsWith(@"\\", StringComparison.Ordinal)) ||
               (text.StartsWith("/", StringComparison.Ordinal) && !text.StartsWith("//", StringComparison.Ordinal));
    }

    private static bool IsAbsoluteWindowsPath(string value) =>
        value.Length >= 3 &&
        char.IsLetter(value[0]) &&
        value[1] == ':' &&
        (value[2] == '\\' || value[2] == '/');

    private static bool HasLocalReferencePropertyScaffold(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        return File.Exists(Path.Combine(projectDirectory, "local.build.props")) ||
               File.Exists(Path.Combine(projectDirectory, "local.build.props.example"));
    }

    private static string FormatReferenceEvidence(ReferenceInfo reference)
    {
        string evidence = $"{reference.Include} -> {reference.HintPath}";
        if (!string.IsNullOrWhiteSpace(reference.SourcePath))
        {
            evidence += $" ({(reference.Imported ? "imported" : "project")} {reference.SourcePath})";
        }

        return evidence;
    }

    private static IReadOnlyList<MsBuildElement> ExpandProjectElements(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        var elements = new List<MsBuildElement>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddProjectElements(projectPath, projectDirectory, rootProjectPath: Path.GetFullPath(projectPath), elements, visited);
        return elements;
    }

    private static void AddProjectElements(
        string projectPath,
        string projectDirectory,
        string rootProjectPath,
        List<MsBuildElement> elements,
        HashSet<string> visited)
    {
        string fullPath = Path.GetFullPath(projectPath);
        if (!visited.Add(fullPath) || !File.Exists(fullPath))
        {
            return;
        }

        XDocument document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        string sourceDirectory = Path.GetDirectoryName(fullPath)!;
        foreach (XElement element in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if (IsNamed("Import")(element))
            {
                string? importPath = ResolveImportPath(element, sourceDirectory, projectDirectory);
                if (importPath is not null)
                {
                    AddProjectElements(importPath, projectDirectory, rootProjectPath, elements, visited);
                }

                continue;
            }

            bool imported = !string.Equals(fullPath, rootProjectPath, StringComparison.OrdinalIgnoreCase);
            elements.Add(new MsBuildElement(element, fullPath, imported));
            elements.AddRange(element.Descendants().Select(descendant => new MsBuildElement(descendant, fullPath, imported)));
        }
    }

    private static string? ResolveImportPath(XElement import, string sourceDirectory, string projectDirectory)
    {
        string? project = import.Attribute("Project")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(project) ||
            project.Contains("$(", StringComparison.Ordinal) ||
            project.Contains('*', StringComparison.Ordinal) ||
            project.Contains('?', StringComparison.Ordinal))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(Path.Combine(sourceDirectory, project));
        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        if (!fullPath.StartsWith(fullProjectDirectory, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            return null;
        }

        string? condition = import.Attribute("Condition")?.Value;
        if (!ImportConditionApplies(condition, fullPath))
        {
            return null;
        }

        return fullPath;
    }

    private static bool ImportConditionApplies(string? condition, string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        Match existsMatch = Regex.Match(condition, @"^\s*Exists\s*\(\s*'(?<path>[^']+)'\s*\)\s*$", RegexOptions.IgnoreCase);
        if (existsMatch.Success)
        {
            return File.Exists(resolvedPath);
        }

        return false;
    }

    private static bool ConditionApplies(
        string? condition,
        string configurationName,
        IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        foreach (string orPart in SplitCondition(condition, @"\s*(?:\|\||\bor\b)\s*"))
        {
            bool allAndPartsApply = SplitCondition(orPart, @"\s*(?:&&|\band\b)\s*")
                .All(part => SingleConditionApplies(part.Trim(), configurationName, properties));
            if (allAndPartsApply)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SingleConditionApplies(
        string condition,
        string configurationName,
        IReadOnlyDictionary<string, string> properties)
    {
        if (condition.StartsWith("!", StringComparison.Ordinal))
        {
            return !SingleConditionApplies(condition[1..].Trim(), configurationName, properties);
        }

        if (TryEvaluateConfigurationCondition(condition, configurationName, out bool configurationConditionApplies))
        {
            return configurationConditionApplies;
        }

        if (TryEvaluateMsBuildBooleanExpression(condition, out bool booleanExpressionApplies))
        {
            return booleanExpressionApplies;
        }

        string[] simpleConfigurationNames = SimpleConfigurationConditionRegex.Matches(condition)
            .Select(match => match.Groups["name"].Value)
            .Where(IsValidConfigurationName)
            .ToArray();
        if (simpleConfigurationNames.Length > 0)
        {
            return simpleConfigurationNames.Any(name =>
                string.Equals(name, configurationName, StringComparison.OrdinalIgnoreCase));
        }

        Match propertyFunctionMatch = Regex.Match(
            condition,
            @"^\s*\$\((?<property>[A-Za-z_][A-Za-z0-9_]*)\.(?<method>Contains|StartsWith|EndsWith)\('(?<needle>[^']*)'\)\)\s*$",
            RegexOptions.IgnoreCase);
        if (propertyFunctionMatch.Success)
        {
            string value = properties.GetValueOrDefault(propertyFunctionMatch.Groups["property"].Value) ?? string.Empty;
            string needle = propertyFunctionMatch.Groups["needle"].Value;
            return propertyFunctionMatch.Groups["method"].Value.ToLowerInvariant() switch
            {
                "contains" => value.Contains(needle, StringComparison.OrdinalIgnoreCase),
                "startswith" => value.StartsWith(needle, StringComparison.OrdinalIgnoreCase),
                "endswith" => value.EndsWith(needle, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        Match comparisonMatch = Regex.Match(
            condition,
            @"^\s*'(?<left>[^']*)'\s*(?<operator>==|!=)\s*'(?<right>[^']*)'\s*$",
            RegexOptions.IgnoreCase);
        if (comparisonMatch.Success)
        {
            string left = ExpandConditionValue(comparisonMatch.Groups["left"].Value, properties);
            string right = ExpandConditionValue(comparisonMatch.Groups["right"].Value, properties);
            bool equal = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            return comparisonMatch.Groups["operator"].Value == "==" ? equal : !equal;
        }

        return true;
    }

    private static string ExpandConditionValue(string value, IReadOnlyDictionary<string, string> properties) =>
        TryEvaluateMsBuildBooleanExpression(value, out bool booleanValue)
            ? booleanValue.ToString().ToLowerInvariant()
            : ExpandProperties(value, properties);

    private static bool TryEvaluateMsBuildBooleanExpression(string expression, out bool value)
    {
        Match osPlatformMatch = MsBuildIsOsPlatformRegex.Match(expression);
        if (osPlatformMatch.Success)
        {
            value = CurrentOsMatches(osPlatformMatch.Groups["platform"].Value);
            return true;
        }

        value = false;
        return false;
    }

    private static bool CurrentOsMatches(string platform)
    {
        return platform.Trim().ToLowerInvariant() switch
        {
            "windows" => OperatingSystem.IsWindows(),
            "linux" => OperatingSystem.IsLinux(),
            "osx" or "macos" => OperatingSystem.IsMacOS(),
            "freebsd" => OperatingSystem.IsFreeBSD(),
            _ => false
        };
    }

    private static IEnumerable<string> ExtractConfigurationNames(string condition)
    {
        foreach (Match match in QuotedComparisonRegex.Matches(condition))
        {
            if (!match.Groups["operator"].Value.Equals("==", StringComparison.Ordinal))
            {
                continue;
            }

            string? configurationName = ExtractConfigurationComparisonValue(
                match.Groups["left"].Value,
                match.Groups["right"].Value,
                allowEmpty: false);
            if (configurationName is not null)
            {
                yield return configurationName;
            }
        }

        foreach (Match match in SimpleConfigurationConditionRegex.Matches(condition))
        {
            string configurationName = match.Groups["name"].Value;
            if (IsValidConfigurationName(configurationName))
            {
                yield return configurationName;
            }
        }
    }

    private static bool TryEvaluateConfigurationCondition(string condition, string configurationName, out bool applies)
    {
        Match match = QuotedComparisonRegex.Match(condition);
        if (!match.Success)
        {
            applies = false;
            return false;
        }

        string? expectedConfigurationName = ExtractConfigurationComparisonValue(
            match.Groups["left"].Value,
            match.Groups["right"].Value,
            allowEmpty: true);
        if (expectedConfigurationName is null)
        {
            applies = false;
            return false;
        }

        bool equal = string.Equals(expectedConfigurationName, configurationName, StringComparison.OrdinalIgnoreCase);
        applies = match.Groups["operator"].Value.Equals("==", StringComparison.Ordinal)
            ? equal
            : !equal;
        return true;
    }

    private static string? ExtractConfigurationComparisonValue(string left, string right, bool allowEmpty)
    {
        string[] leftParts = left.Split('|', StringSplitOptions.TrimEntries);
        string[] rightParts = right.Split('|', StringSplitOptions.TrimEntries);
        int configurationPartIndex = Array.FindIndex(leftParts, part =>
            part.Contains("$(Configuration)", StringComparison.OrdinalIgnoreCase));
        if (configurationPartIndex < 0 ||
            configurationPartIndex >= rightParts.Length)
        {
            return null;
        }

        string configurationName = rightParts[configurationPartIndex].Trim();
        if (!allowEmpty && !IsValidConfigurationName(configurationName))
        {
            return null;
        }

        return configurationName;
    }

    private static bool IsValidConfigurationName(string configurationName) =>
        !string.IsNullOrWhiteSpace(configurationName) &&
        !configurationName.Equals("=", StringComparison.Ordinal) &&
        !configurationName.Contains("$(", StringComparison.Ordinal);

    private static IEnumerable<string> SplitCondition(string condition, string pattern) =>
        Regex.Split(condition, pattern, RegexOptions.IgnoreCase)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0);

    private static string ExpandProperties(string value, IReadOnlyDictionary<string, string> properties) =>
        Regex.Replace(
            value,
            @"\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)",
            match => properties.TryGetValue(match.Groups["name"].Value, out string? replacement) ? replacement : string.Empty);

    private static Func<XElement, bool> IsNamed(string localName) =>
        element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitMsBuildList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsTargetFramework(string? actual, params string[] expected) =>
        actual is not null && expected.Any(value => string.Equals(actual, value, StringComparison.OrdinalIgnoreCase));

    private static string? CombineConditions(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? null : right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return $"{left} && {right}";
    }

    private sealed record RuntimeScores(int Mono, int Il2Cpp, int CrossCompat, IReadOnlyList<string> Evidence);

    private enum RuntimeNameIntent
    {
        None,
        Mono,
        Il2Cpp
    }

    private sealed record MsBuildElement(XElement Element, string SourcePath, bool Imported);

    private sealed class SourceConditionalSymbols
    {
        public static readonly SourceConditionalSymbols Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

        private readonly IReadOnlyDictionary<string, string> evidenceBySymbol;

        public SourceConditionalSymbols(IReadOnlyDictionary<string, string> evidenceBySymbol)
        {
            this.evidenceBySymbol = evidenceBySymbol;
        }

        public bool Contains(string symbol) => evidenceBySymbol.ContainsKey(symbol);

        public string GetEvidence(string symbol) =>
            evidenceBySymbol.TryGetValue(symbol, out string? evidence) ? evidence : $"source uses {symbol}";
    }
}
