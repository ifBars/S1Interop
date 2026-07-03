using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core;

public sealed class MigrationApplier
{
    private static readonly Regex ConfigurationConditionRegex = new(
        @"\$\(\s*Configuration\s*\)\s*'?\s*={1,2}\s*(?:'(?<name>[^'|]+)(?:\|[^']*)?'|(?<name>[^'""\)\s|]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QuotedComparisonRegex = new(
        @"'(?<left>[^']*)'\s*(?<operator>==|!=)\s*'(?<right>[^']*)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SourceDiagnosticEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<member>[A-Za-z_][A-Za-z0-9_]*\.(?<method>[A-Za-z_][A-Za-z0-9_]*))(?:\((?<params>[^)]*)\))?\s+uses\s+",
        RegexOptions.Compiled);

    private static readonly Regex SourceClassDiagnosticEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex MsBuildPropertyReferenceRegex = new(
        @"\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex ClassDeclarationRegex = new(
        @"^(?<indent>\s*)(?:public|internal|private|protected)?\s*(?:(?:sealed|abstract|partial)\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*(?<base>[^{\r\n]+))?",
        RegexOptions.Compiled);

    public MigrationApplyResult Apply(MigrationPlan plan)
    {
        string runId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..30];
        string runRoot = Path.Combine(GetRunBasePath(plan.RootPath), "s1interop-runs", runId);
        string backupRoot = Path.Combine(runRoot, "backups");
        Directory.CreateDirectory(backupRoot);

        var fileChanges = new List<MigrationFileChange>();
        var appliedOperations = new List<MigrationOperation>();

        foreach (ProjectMigrationPlan projectPlan in plan.Projects)
        {
            if (projectPlan.Operations.Count == 0)
            {
                continue;
            }

            ApplyProject(projectPlan, backupRoot, fileChanges, appliedOperations);
        }

        string manifestPath = Path.Combine(runRoot, "manifest.json");
        var result = new MigrationApplyResult(runId, manifestPath, appliedOperations, fileChanges);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(result, S1InteropJsonContext.Default.MigrationApplyResult),
            Encoding.UTF8);

        return result;
    }

    private static void ApplyProject(
        ProjectMigrationPlan projectPlan,
        string backupRoot,
        List<MigrationFileChange> fileChanges,
        List<MigrationOperation> appliedOperations)
    {
        string projectPath = projectPlan.ProjectPath;

        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        bool projectDocumentChanged = false;
        bool projectTracked = false;

        foreach (MigrationOperation operation in projectPlan.Operations
                     .Where(operation => operation.Automatic)
                     .OrderBy(GetApplyPriority))
        {
            bool mutatesProjectDocument =
                !operation.RuleId.Equals("generate_unity_event_bridge", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("generate_delegate_event_bridge", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("generate_harmony_method_targets", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("generate_member_access_targets", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("generate_backend_neutral_starter", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("generate_source_risk_report", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("conditionalize_scheduleone_usings", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_fully_qualified_scheduleone_types", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_unity_event_listeners", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_delegate_assignments", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_delegate_arguments", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_harmony_overload_bindings", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_member_access_fallbacks", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_direct_member_reflection_lookups", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("rewrite_il2cpp_object_casts", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("injected_type_missing_registertype", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("injected_type_missing_intptr_constructor", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("injected_member_requires_hidefromil2cpp", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("game_constructor_requires_il2cpp_signature", StringComparison.OrdinalIgnoreCase) &&
                !operation.RuleId.Equals("install_build_validation_hook", StringComparison.OrdinalIgnoreCase);
            if (mutatesProjectDocument && !projectTracked)
            {
                TrackExistingFile(projectPath, backupRoot, fileChanges);
                projectTracked = true;
            }

            bool operationChanged = operation.RuleId switch
            {
                "wrong_target_framework" => ApplyTargetFramework(document, operation),
                "wrong_il2cpp_reference_surface" => ApplyIl2CppReferenceSurface(document, operation.Configuration),
                "stale_publicized_surface" => ApplyStalePublicizedSurface(document, operation.Configuration),
                "reference_should_not_copy_local" => ApplyPrivateFalse(document, operation.Configuration),
                "local_path_in_project" => ApplyLocalPathMove(projectPath, document, backupRoot, fileChanges),
                "global_usings_require_langversion" => ApplyGlobalUsingsLangVersion(document),
                "missing_local_reference_properties" => ApplyLocalReferencePropertyScaffold(projectPath, document, backupRoot, fileChanges),
                "missing_local_build_props_import" => ApplyLocalBuildPropsImport(document),
                "missing_il2cppinterop_reference" => ApplyMissingIl2CppInteropReference(document, operation.Configuration),
                "missing_runtime_define" => ApplyMissingRuntimeDefine(document, operation),
                "add_il2cpp_configuration" => ApplyDualRuntimeScaffold(projectPath, document, operation, backupRoot, fileChanges),
                "condition_imported_mono_runtime_flags" => ApplyImportedMonoRuntimeFlagConditioning(projectPath, document, backupRoot, fileChanges),
                "install_s1interop_generator_package" => ApplyS1InteropGeneratorPackageReference(document),
                "conditionalize_scheduleone_usings" => ApplyScheduleOneUsingConditionalization(operation.FilePath, backupRoot, fileChanges),
                "rewrite_fully_qualified_scheduleone_types" => ApplyFullyQualifiedScheduleOneTypeRewrite(projectPath, operation.FilePath, backupRoot, fileChanges),
                "injected_type_missing_registertype" => ApplyInjectedTypeRegistration(operation, backupRoot, fileChanges),
                "injected_type_missing_intptr_constructor" => ApplyInjectedTypeIntPtrConstructor(operation, backupRoot, fileChanges),
                "injected_member_requires_hidefromil2cpp" => ApplyHideFromIl2CppAttribute(operation, backupRoot, fileChanges),
                "game_constructor_requires_il2cpp_signature" => ApplyGameConstructorSignature(operation, backupRoot, fileChanges),
                "generate_sdk_facade" => ApplySdkFacade(projectPlan, operation, document, backupRoot, fileChanges),
                "generate_full_sdk_facade" => ApplySdkFacade(projectPlan, operation, document, backupRoot, fileChanges),
                "generate_unity_event_bridge" => ApplyUnityEventBridge(operation, backupRoot, fileChanges),
                "generate_delegate_event_bridge" => ApplyDelegateEventBridge(operation, backupRoot, fileChanges),
                "generate_harmony_method_targets" => ApplyHarmonyMethodTargets(projectPlan, document, operation, backupRoot, fileChanges),
                "generate_member_access_targets" => ApplyMemberAccessTargets(projectPlan, document, operation, backupRoot, fileChanges),
                "generate_backend_neutral_starter" => ApplyBackendNeutralStarter(operation, backupRoot, fileChanges),
                "generate_source_risk_report" => ApplySourceRiskReport(projectPlan, operation, backupRoot, fileChanges),
                "rewrite_unity_event_listeners" => ApplyUnityEventListenerRewrite(operation.FilePath, backupRoot, fileChanges),
                "rewrite_delegate_assignments" => ApplyDelegateAssignmentRewrite(operation.FilePath, backupRoot, fileChanges),
                "rewrite_delegate_arguments" => ApplyDelegateArgumentRewrite(operation.FilePath, backupRoot, fileChanges),
                "rewrite_harmony_overload_bindings" => ApplyHarmonyOverloadBindingRewrite(projectPlan.ProjectPath, operation.FilePath, backupRoot, fileChanges),
                "rewrite_member_access_fallbacks" => ApplyMemberAccessFallbackRewrite(projectPlan.ProjectPath, operation.FilePath, backupRoot, fileChanges),
                "rewrite_direct_member_reflection_lookups" => ApplyDirectMemberReflectionLookupRewrite(projectPlan.ProjectPath, operation.FilePath, backupRoot, fileChanges),
                "rewrite_il2cpp_object_casts" => ApplyIl2CppObjectCastRewrite(operation.FilePath, backupRoot, fileChanges),
                "install_build_validation_hook" => ApplyBuildValidationHook(projectPath, document, backupRoot, fileChanges),
                _ => false
            };

            if (operationChanged)
            {
                projectDocumentChanged |= mutatesProjectDocument;
                appliedOperations.Add(operation);
            }
        }

        if (projectDocumentChanged)
        {
            document.Save(projectPath);
            UpdateTrackedFileHash(projectPath, fileChanges);
        }
    }

    private static bool ApplyDualRuntimeScaffold(
        string projectPath,
        XDocument document,
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        bool projectChanged = DualRuntimeProjectScaffolder.Apply(document, GetMonoConfigurationsFromEvidence(operation.Evidence));
        EnsureDualRuntimeLocalProps(projectPath, backupRoot, fileChanges);
        ConditionImportedMonoRuntimeFlags(projectPath, document, backupRoot, fileChanges);
        ApplySolutionConfigurationScaffold(projectPath, document, backupRoot, fileChanges);
        return projectChanged;
    }

    private static bool ApplyImportedMonoRuntimeFlagConditioning(
        string projectPath,
        XDocument document,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        int before = fileChanges.Count(change => change.FilePath.EndsWith("conditions.props", StringComparison.OrdinalIgnoreCase));
        bool projectChanged = ConditionMonoReferenceImports(document);
        ConditionImportedMonoRuntimeFlags(projectPath, document, backupRoot, fileChanges);
        int after = fileChanges.Count(change => change.FilePath.EndsWith("conditions.props", StringComparison.OrdinalIgnoreCase));
        return projectChanged || after > before;
    }

    private static bool ConditionMonoReferenceImports(XDocument document)
    {
        bool changed = false;
        string monoCondition = BuildNonIl2CppConfigurationCondition(document);
        foreach (XElement import in document.Root!.Elements().Where(IsNamed("Import")))
        {
            string project = import.Attribute("Project")?.Value ?? string.Empty;
            if (!project.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                project.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            XAttribute? condition = import.Attribute("Condition");
            if (condition is null)
            {
                import.SetAttributeValue("Condition", monoCondition);
                changed = true;
                continue;
            }

            if (condition.Value.Contains("S1InteropTargetRuntime", StringComparison.OrdinalIgnoreCase))
            {
                condition.Value = monoCondition;
                changed = true;
            }
            else if (!condition.Value.Contains("Il2cpp", StringComparison.OrdinalIgnoreCase))
            {
                condition.Value = $"({condition.Value}) and {monoCondition}";
                changed = true;
            }
        }

        return changed;
    }

    private static string BuildNonIl2CppConfigurationCondition(XDocument document)
    {
        string[] il2CppConfigurations = document.Descendants()
            .Where(IsNamed("Configurations"))
            .SelectMany(element => SplitMsBuildList(element.Value))
            .Where(IsIl2CppConfiguration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return il2CppConfigurations.Length == 0
            ? "'$(S1InteropTargetRuntime)' != 'Il2Cpp'"
            : string.Join(" and ", il2CppConfigurations.Select(configuration => $"'$(Configuration)' != '{configuration}'"));
    }

    private static void ConditionImportedMonoRuntimeFlags(
        string projectPath,
        XDocument projectDocument,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (XElement import in projectDocument.Root!.Elements().Where(IsNamed("Import")))
        {
            string? importProject = import.Attribute("Project")?.Value;
            if (string.IsNullOrWhiteSpace(importProject) ||
                importProject.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            string importedPath = Path.GetFullPath(Path.Combine(projectDirectory, importProject));
            if (!importedPath.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(importedPath))
            {
                continue;
            }

            XDocument importedDocument;
            try
            {
                importedDocument = XDocument.Load(importedPath, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            bool changed = false;
            string monoRuntimeCondition = BuildNonIl2CppConfigurationCondition(projectDocument);
            foreach (XElement isMono in importedDocument.Descendants().Where(IsNamed("IsMono")))
            {
                if (!isMono.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                XAttribute? condition = isMono.Attribute("Condition");
                if (condition is null ||
                    condition.Value.Contains("S1InteropTargetRuntime", StringComparison.OrdinalIgnoreCase))
                {
                    isMono.SetAttributeValue("Condition", monoRuntimeCondition);
                    changed = true;
                }
            }

            if (!changed)
            {
                continue;
            }

            TrackFile(importedPath, backupRoot, fileChanges);
            importedDocument.Save(importedPath);
            UpdateTrackedFileHash(importedPath, fileChanges);
        }
    }

    private static IReadOnlyList<string>? GetMonoConfigurationsFromEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return null;
        }

        const string marker = "mono_configurations=";
        int index = evidence.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        string value = evidence[(index + marker.Length)..].Trim();
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int GetApplyPriority(MigrationOperation operation) =>
        operation.RuleId switch
        {
            "generate_sdk_facade" => 10,
            "generate_full_sdk_facade" => 10,
            "generate_unity_event_bridge" => 10,
            "generate_harmony_method_targets" => 10,
            "generate_member_access_targets" => 10,
            "generate_backend_neutral_starter" => 10,
            "install_s1interop_generator_package" => 10,
            "rewrite_fully_qualified_scheduleone_types" => 20,
            "conditionalize_scheduleone_usings" => 20,
            "rewrite_unity_event_listeners" => 20,
            "rewrite_harmony_overload_bindings" => 20,
            "rewrite_delegate_arguments" => 20,
            "rewrite_member_access_fallbacks" => 20,
            "rewrite_direct_member_reflection_lookups" => 20,
            "rewrite_il2cpp_object_casts" => 20,
            "injected_type_missing_registertype" => 20,
            "injected_type_missing_intptr_constructor" => 21,
            "injected_member_requires_hidefromil2cpp" => 22,
            "game_constructor_requires_il2cpp_signature" => 23,
            _ => 0
        };

    private static bool ApplyTargetFramework(XDocument document, MigrationOperation operation)
    {
        string? configuration = operation.Configuration;
        if (configuration is null)
        {
            return false;
        }

        string targetFramework = GetTargetFrameworkForOperation(operation);
        XElement? propertyGroup = GetConfigurationPropertyGroups(document, configuration)
            .FirstOrDefault(group => group.Elements().Any(IsNamed("TargetFramework")));
        XElement? target = propertyGroup?.Elements().FirstOrDefault(IsNamed("TargetFramework"));
        if (target is null)
        {
            propertyGroup = new XElement(
                "PropertyGroup",
                new XAttribute("Condition", $"'$(Configuration)'=='{configuration}'"));
            document.Root!.Add(propertyGroup);
            propertyGroup.Add(new XElement("TargetFramework", targetFramework));
            return true;
        }

        if (string.Equals(target.Value.Trim(), targetFramework, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        target.Value = targetFramework;
        return true;
    }

    private static bool ApplyGlobalUsingsLangVersion(XDocument document)
    {
        XElement? langVersion = document.Descendants().FirstOrDefault(IsNamed("LangVersion"));
        if (langVersion is not null)
        {
            if (string.Equals(langVersion.Value.Trim(), "10.0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            langVersion.Value = "10.0";
            return true;
        }

        XElement propertyGroup = document.Root!.Elements()
                                     .FirstOrDefault(IsNamed("PropertyGroup")) ??
                                 new XElement("PropertyGroup");
        if (propertyGroup.Parent is null)
        {
            document.Root!.AddFirst(propertyGroup);
        }

        propertyGroup.Add(new XElement("LangVersion", "10.0"));
        return true;
    }

    private static bool ApplyS1InteropGeneratorPackageReference(XDocument document)
    {
        XElement? existing = document.Descendants()
            .Where(IsNamed("PackageReference"))
            .FirstOrDefault(package =>
                string.Equals(package.Attribute("Include")?.Value, "S1Interop.Generators", StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            bool changed = false;
            changed |= SetAttributeIfDifferent(existing, "Version", "0.1.0-alpha.1");
            changed |= SetAttributeIfDifferent(existing, "PrivateAssets", "all");
            changed |= SetAttributeIfDifferent(existing, "IncludeAssets", "runtime; build; native; contentfiles; analyzers; buildtransitive");
            return changed;
        }

        XElement itemGroup = document.Root!.Elements()
            .Where(IsNamed("ItemGroup"))
            .FirstOrDefault(group => group.Attribute("Condition") is null && group.Elements().Any(IsNamed("PackageReference")))
            ?? new XElement("ItemGroup");
        if (itemGroup.Parent is null)
        {
            document.Root.Add(itemGroup);
        }

        itemGroup.Add(
            new XElement(
                "PackageReference",
                new XAttribute("Include", "S1Interop.Generators"),
                new XAttribute("Version", "0.1.0-alpha.1"),
                new XAttribute("PrivateAssets", "all"),
                new XAttribute("IncludeAssets", "runtime; build; native; contentfiles; analyzers; buildtransitive")));
        return true;
    }

    private static bool SetAttributeIfDifferent(XElement element, string name, string value)
    {
        XAttribute? attribute = element.Attribute(name);
        if (attribute is not null)
        {
            if (string.Equals(attribute.Value, value, StringComparison.Ordinal))
            {
                return false;
            }

            attribute.Value = value;
            return true;
        }

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool ApplyIl2CppReferenceSurface(XDocument document, string? configuration)
    {
        if (configuration is null)
        {
            return false;
        }

        bool changed = false;
        foreach (XElement reference in GetReferencesForConfiguration(document, configuration))
        {
            XElement? hintPath = reference.Elements().FirstOrDefault(IsNamed("HintPath"));
            if (hintPath is null ||
                !hintPath.Value.Contains(@"Schedule I_Data\Managed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string include = reference.Attribute("Include")?.Value ?? string.Empty;
            if (include.Equals("Il2CppAssembly-CSharp", StringComparison.OrdinalIgnoreCase))
            {
                reference.SetAttributeValue("Include", "Assembly-CSharp");
                include = "Assembly-CSharp";
            }

            string newPath = hintPath.Value
                .Replace(@"Schedule I_Data\Managed", @"MelonLoader\Il2CppAssemblies", StringComparison.OrdinalIgnoreCase)
                .Replace("Il2CppAssembly-CSharp.dll", "Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase);

            if (!newPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                newPath = Path.Combine(newPath, $"{include}.dll");
            }

            if (!string.Equals(hintPath.Value, newPath, StringComparison.Ordinal))
            {
                hintPath.Value = newPath;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ApplyStalePublicizedSurface(XDocument document, string? configuration)
    {
        if (configuration is null)
        {
            return false;
        }

        bool changed = false;
        foreach (XElement reference in GetReferencesForConfiguration(document, configuration))
        {
            XElement? hintPath = reference.Elements().FirstOrDefault(IsNamed("HintPath"));
            if (hintPath is null ||
                !hintPath.Value.Contains("Assembly-CSharp-publicized", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string include = reference.Attribute("Include")?.Value ?? string.Empty;
            if (include.Equals("Assembly-CSharp-publicized", StringComparison.OrdinalIgnoreCase))
            {
                reference.SetAttributeValue("Include", "Assembly-CSharp");
                changed = true;
            }

            string rewrittenHintPath = hintPath.Value.Replace(
                "Assembly-CSharp-publicized.dll",
                "Assembly-CSharp.dll",
                StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(hintPath.Value, rewrittenHintPath, StringComparison.Ordinal))
            {
                hintPath.Value = rewrittenHintPath;
                changed = true;
            }

            changed |= EnsurePublicizeItem(reference);
        }

        if (changed)
        {
            EnsureKrafsPublicizerPackageReference(document);
        }

        return changed;
    }

    private static bool EnsurePublicizeItem(XElement reference)
    {
        XElement? itemGroup = reference.Parent;
        if (itemGroup is null)
        {
            return false;
        }

        string? referenceCondition = reference.Attribute("Condition")?.Value;
        bool exists = itemGroup.Elements().Where(IsNamed("Publicize")).Any(publicize =>
            string.Equals(publicize.Attribute("Include")?.Value, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(publicize.Attribute("Condition")?.Value, referenceCondition, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return false;
        }

        var publicize = new XElement("Publicize", new XAttribute("Include", "Assembly-CSharp"));
        if (!string.IsNullOrWhiteSpace(referenceCondition))
        {
            publicize.SetAttributeValue("Condition", referenceCondition);
        }

        reference.AddAfterSelf(publicize);
        return true;
    }

    private static void EnsureKrafsPublicizerPackageReference(XDocument document)
    {
        bool exists = document.Descendants()
            .Where(IsNamed("PackageReference"))
            .Any(packageReference =>
                string.Equals(packageReference.Attribute("Include")?.Value, "Krafs.Publicizer", StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return;
        }

        XElement? itemGroup = document.Root!.Elements()
            .Where(IsNamed("ItemGroup"))
            .FirstOrDefault(group => group.Elements().Any(IsNamed("PackageReference")));
        if (itemGroup is null)
        {
            itemGroup = new XElement("ItemGroup");
            document.Root!.Add(itemGroup);
        }

        itemGroup.Add(new XElement(
            "PackageReference",
            new XAttribute("Include", "Krafs.Publicizer"),
            new XAttribute("Version", "2.3.0"),
            new XElement("PrivateAssets", "all"),
            new XElement("IncludeAssets", "runtime; build; native; contentfiles; analyzers; buildtransitive")));
    }

    private static bool ApplyPrivateFalse(XDocument document, string? configuration)
    {
        bool changed = false;
        IEnumerable<XElement> references = configuration is null
            ? document.Descendants().Where(IsNamed("Reference"))
            : GetReferencesForConfiguration(document, configuration);

        foreach (XElement reference in references)
        {
            if (!IsGameReference(reference) ||
                reference.Elements().Where(IsNamed("Private")).Any(element =>
                    string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            reference.Add(new XElement("Private", "false"));
            changed = true;
        }

        return changed;
    }

    private static bool ApplyLocalPathMove(
        string projectPath,
        XDocument document,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        var movedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AbsolutePropertyValue[] absoluteProperties = document.Descendants()
            .Where(IsNamed("PropertyGroup"))
            .SelectMany(propertyGroup => propertyGroup.Elements().Where(element => !element.HasElements)
                .Select(property => new AbsolutePropertyValue(
                    property,
                    property.Name.LocalName,
                    property.Value.Trim(),
                    property.Attribute("Condition")?.Value,
                    propertyGroup.Attribute("Condition")?.Value)))
            .Where(property => IsAbsoluteWindowsPath(property.Value))
            .ToArray();
        Dictionary<string, int> distinctValueCountsByPropertyName = absoluteProperties
            .GroupBy(property => property.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(property => property.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.OrdinalIgnoreCase);
        var usedPropertyNames = new HashSet<string>(
            document.Descendants().Select(element => element.Name.LocalName),
            StringComparer.OrdinalIgnoreCase);

        foreach (AbsolutePropertyValue property in absoluteProperties)
        {
            string localPropertyName = distinctValueCountsByPropertyName[property.PropertyName] > 1
                ? CreateScopedLocalPropertyName(property, usedPropertyNames)
                : property.PropertyName;
            movedProperties[localPropertyName] = property.Value;

            if (string.Equals(localPropertyName, property.PropertyName, StringComparison.OrdinalIgnoreCase))
            {
                property.Element.Value = string.Empty;
                if (property.Element.Attribute("Condition") is null)
                {
                    property.Element.SetAttributeValue("Condition", $"'$({property.PropertyName})'==''");
                }

                continue;
            }

            property.Element.Value = $"$({localPropertyName})";
            if (property.Element.Attribute("Condition") is null &&
                string.IsNullOrWhiteSpace(property.PropertyGroupCondition))
            {
                property.Element.SetAttributeValue("Condition", $"'$({property.PropertyName})'==''");
            }
        }

        bool hintPathsChanged = RewriteAbsoluteHintPaths(document, movedProperties);

        if (movedProperties.Count == 0 && !hintPathsChanged)
        {
            return false;
        }

        EnsureLocalBuildPropsImport(document);
        EnsureProjectPropertyDefaults(document, movedProperties.Keys);

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string localPropsPath = Path.Combine(projectDirectory, "local.build.props");
        string examplePropsPath = Path.Combine(projectDirectory, "local.build.props.example");
        string gitIgnorePath = Path.Combine(projectDirectory, ".gitignore");

        WriteLocalProps(localPropsPath, movedProperties, includeValues: true, backupRoot, fileChanges);
        WriteLocalProps(examplePropsPath, movedProperties, includeValues: false, backupRoot, fileChanges);
        EnsureGitIgnoreEntry(gitIgnorePath, "local.build.props", backupRoot, fileChanges);
        return true;
    }

    private static bool ApplyLocalReferencePropertyScaffold(
        string projectPath,
        XDocument document,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        SortedDictionary<string, string> properties = DiscoverLocalReferenceProperties(document);
        if (properties.Count == 0)
        {
            return false;
        }

        EnsureLocalBuildPropsImport(document);

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string localPropsPath = Path.Combine(projectDirectory, "local.build.props");
        string examplePropsPath = Path.Combine(projectDirectory, "local.build.props.example");
        string gitIgnorePath = Path.Combine(projectDirectory, ".gitignore");

        WriteLocalProps(localPropsPath, properties, includeValues: false, backupRoot, fileChanges);
        WriteLocalProps(examplePropsPath, properties, includeValues: false, backupRoot, fileChanges);
        EnsureGitIgnoreEntry(gitIgnorePath, "local.build.props", backupRoot, fileChanges);
        return true;
    }

    private static bool ApplyLocalBuildPropsImport(XDocument document)
    {
        int before = document.Root!.Elements().Where(IsNamed("Import")).Count();
        EnsureLocalBuildPropsImport(document);
        int after = document.Root!.Elements().Where(IsNamed("Import")).Count();
        return after > before;
    }

    private static void EnsureDualRuntimeLocalProps(
        string projectPath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string localPropsPath = Path.Combine(projectDirectory, "local.build.props");
        string examplePropsPath = Path.Combine(projectDirectory, "local.build.props.example");
        string gitIgnorePath = Path.Combine(projectDirectory, ".gitignore");

        string monoGamePath = TryReadLocalProperty(localPropsPath, "GamePath") ?? string.Empty;
        EnsureLocalPropsProperties(
            localPropsPath,
            new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MonoGamePath"] = monoGamePath,
                ["Il2CppGamePath"] = string.Empty
            },
            backupRoot,
            fileChanges);
        EnsureLocalPropsProperties(
            examplePropsPath,
            new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["GamePath"] = string.Empty,
                ["MonoGamePath"] = string.Empty,
                ["Il2CppGamePath"] = string.Empty
            },
            backupRoot,
            fileChanges);
        EnsureGitIgnoreEntry(gitIgnorePath, "local.build.props", backupRoot, fileChanges);
    }

    private static string? TryReadLocalProperty(string path, string propertyName)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            return document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim();
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureLocalPropsProperties(
        string path,
        IReadOnlyDictionary<string, string> properties,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        XDocument document;
        XElement propertyGroup;
        if (File.Exists(path))
        {
            document = XDocument.Load(path);
            propertyGroup = document.Root?.Elements().FirstOrDefault(IsNamed("PropertyGroup"))
                ?? new XElement("PropertyGroup");
            if (propertyGroup.Parent is null)
            {
                document.Root!.Add(propertyGroup);
            }
        }
        else
        {
            propertyGroup = new XElement("PropertyGroup");
            document = new XDocument(new XElement("Project", propertyGroup));
        }

        bool changed = false;
        foreach (KeyValuePair<string, string> property in properties)
        {
            bool exists = propertyGroup.Elements().Any(element =>
                string.Equals(element.Name.LocalName, property.Key, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                continue;
            }

            propertyGroup.Add(new XElement(property.Key, property.Value));
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        TrackFile(path, backupRoot, fileChanges);
        document.Save(path);
        UpdateTrackedFileHash(path, fileChanges);
    }

    private static void ApplySolutionConfigurationScaffold(
        string projectPath,
        XDocument document,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        foreach (SolutionConfigurationScaffoldResult result in SolutionConfigurationScaffolder.Scaffold(projectPath, document))
        {
            TrackFile(result.SolutionPath, backupRoot, fileChanges);
            File.WriteAllText(result.SolutionPath, result.UpdatedText, Encoding.UTF8);
            UpdateTrackedFileHash(result.SolutionPath, fileChanges);
        }
    }

    private static SortedDictionary<string, string> DiscoverLocalReferenceProperties(XDocument document)
    {
        var propertyAssignments = document.Descendants()
            .Where(IsNamed("PropertyGroup"))
            .SelectMany(propertyGroup => propertyGroup.Elements().Where(element => !element.HasElements))
            .GroupBy(property => property.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(property => property.Value.Trim()).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var properties = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement hintPath in document.Descendants().Where(IsNamed("HintPath")))
        {
            string value = hintPath.Value.Trim();
            if (!CanCollapseToRootRelativePath(value))
            {
                continue;
            }

            foreach (string propertyName in GetPropertyReferences(value))
            {
                AddTerminalLocalReferenceProperties(propertyName, propertyAssignments, properties, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        return properties;
    }

    private static bool CanCollapseToRootRelativePath(string value)
    {
        if (!value.Contains("$(", StringComparison.Ordinal))
        {
            return false;
        }

        string collapsed = MsBuildPropertyReferenceRegex.Replace(value, string.Empty).Trim();
        return IsRootRelativePath(collapsed);
    }

    private static void AddTerminalLocalReferenceProperties(
        string propertyName,
        IReadOnlyDictionary<string, string[]> propertyAssignments,
        SortedDictionary<string, string> properties,
        HashSet<string> visiting)
    {
        if (!visiting.Add(propertyName))
        {
            return;
        }

        if (!propertyAssignments.TryGetValue(propertyName, out string[]? values) || values.Length == 0)
        {
            properties.TryAdd(propertyName, string.Empty);
            visiting.Remove(propertyName);
            return;
        }

        bool addedDependency = false;
        foreach (string value in values)
        {
            string[] dependencies = GetPropertyReferences(value).ToArray();
            if (dependencies.Length == 0)
            {
                continue;
            }

            addedDependency = true;
            foreach (string dependency in dependencies)
            {
                AddTerminalLocalReferenceProperties(dependency, propertyAssignments, properties, visiting);
            }
        }

        if (!addedDependency)
        {
            properties.TryAdd(propertyName, string.Empty);
        }

        visiting.Remove(propertyName);
    }

    private static IEnumerable<string> GetPropertyReferences(string value) =>
        MsBuildPropertyReferenceRegex.Matches(value)
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsRootRelativePath(string value) =>
        (value.StartsWith(@"\", StringComparison.Ordinal) && !value.StartsWith(@"\\", StringComparison.Ordinal)) ||
        (value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith("//", StringComparison.Ordinal));

    private static string CreateScopedLocalPropertyName(
        AbsolutePropertyValue property,
        HashSet<string> usedPropertyNames)
    {
        string condition = FirstNonEmpty(property.PropertyCondition, property.PropertyGroupCondition) ?? "Default";
        MatchCollection matches = ConfigurationConditionRegex.Matches(condition);
        string suffix = matches.Count == 0
            ? "Default"
            : string.Join(
                "_",
                matches
                    .Cast<Match>()
                    .Select(match => SanitizePropertyNamePart(match.Groups["name"].Value))
                    .Where(part => part.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(suffix))
        {
            suffix = "Default";
        }

        string baseName = $"{property.PropertyName}_{suffix}";
        string candidate = baseName;
        for (int index = 2; usedPropertyNames.Contains(candidate); index++)
        {
            candidate = $"{baseName}_{index}";
        }

        usedPropertyNames.Add(candidate);
        return candidate;
    }

    private static string SanitizePropertyNamePart(string value)
    {
        string sanitized = Regex.Replace(value, @"[^A-Za-z0-9_]", "_");
        return sanitized.Trim('_');
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

    private static bool RewriteAbsoluteHintPaths(XDocument document, Dictionary<string, string> movedProperties)
    {
        bool changed = false;
        var rootProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> movedProperty in movedProperties)
        {
            if (LooksLikeGameRoot(movedProperty.Value))
            {
                rootProperties[movedProperty.Value.TrimEnd('\\', '/')] = movedProperty.Key;
            }
        }

        foreach (XElement hintPath in document.Descendants().Where(IsNamed("HintPath")))
        {
            string value = hintPath.Value.Trim();
            if (!IsAbsoluteWindowsPath(value))
            {
                continue;
            }

            string rewritten;
            if (TryGetGameRoot(value, out string? gameRoot))
            {
                string propertyName = GetOrCreateRootPropertyName(rootProperties, movedProperties, gameRoot);
                string suffix = value[gameRoot.Length..];
                rewritten = $"$({propertyName}){suffix}";
            }
            else
            {
                string propertyName = GetOrCreateAbsoluteHintPathPropertyName(hintPath, movedProperties, value);
                rewritten = $"$({propertyName})";
            }

            if (string.Equals(value, rewritten, StringComparison.Ordinal))
            {
                continue;
            }

            hintPath.Value = rewritten;
            changed = true;
        }

        return changed;
    }

    private static string GetOrCreateAbsoluteHintPathPropertyName(
        XElement hintPath,
        Dictionary<string, string> movedProperties,
        string value)
    {
        string? existing = movedProperties.FirstOrDefault(property =>
            string.Equals(property.Value, value, StringComparison.OrdinalIgnoreCase)).Key;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        string include = hintPath.Parent?.Attribute("Include")?.Value ?? "Reference";
        string simpleInclude = include.Split(',', StringSplitOptions.TrimEntries)[0];
        string sanitizedInclude = SanitizePropertyNamePart(simpleInclude);
        string baseName = string.IsNullOrWhiteSpace(sanitizedInclude)
            ? "ReferenceHintPath"
            : $"{sanitizedInclude}HintPath";
        string propertyName = movedProperties.ContainsKey(baseName)
            ? GetNextPropertyName(movedProperties.Keys, baseName)
            : baseName;
        movedProperties[propertyName] = value;
        return propertyName;
    }

    private static string GetOrCreateRootPropertyName(
        Dictionary<string, string> rootProperties,
        Dictionary<string, string> movedProperties,
        string gameRoot)
    {
        string normalizedRoot = gameRoot.TrimEnd('\\', '/');
        if (rootProperties.TryGetValue(normalizedRoot, out string? existingName))
        {
            return existingName;
        }

        string propertyName = movedProperties.ContainsKey("GamePath")
            ? GetNextPropertyName(movedProperties.Keys, "GamePath")
            : "GamePath";
        movedProperties[propertyName] = normalizedRoot;
        rootProperties[normalizedRoot] = propertyName;
        return propertyName;
    }

    private static string GetNextPropertyName(IEnumerable<string> existingNames, string baseName)
    {
        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        for (int index = 2; ; index++)
        {
            string candidate = $"{baseName}{index}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static void EnsureProjectPropertyDefaults(XDocument document, IEnumerable<string> propertyNames)
    {
        XElement propertyGroup = document.Root!.Elements().FirstOrDefault(IsNamed("PropertyGroup"))
            ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent is null)
        {
            document.Root!.AddFirst(propertyGroup);
        }

        foreach (string propertyName in propertyNames)
        {
            bool exists = document.Descendants().Where(IsNamed(propertyName)).Any();
            if (exists)
            {
                continue;
            }

            propertyGroup.Add(new XElement(
                propertyName,
                new XAttribute("Condition", $"'$({propertyName})'==''"),
                string.Empty));
        }
    }

    private static bool ApplyMissingIl2CppInteropReference(XDocument document, string? configuration)
    {
        if (configuration is null)
        {
            return false;
        }

        bool alreadyExists = GetReferencesForConfiguration(document, configuration).Any(reference =>
            (reference.Attribute("Include")?.Value ?? string.Empty).Equals("Il2CppInterop.Runtime", StringComparison.OrdinalIgnoreCase));
        if (alreadyExists)
        {
            return false;
        }

        XElement itemGroup = GetOrCreateConfigurationItemGroup(document, configuration);
        itemGroup.Add(
            new XElement("Reference",
                new XAttribute("Include", "Il2CppInterop.Runtime"),
                new XElement("HintPath", @"$(GamePath)\MelonLoader\net6\Il2CppInterop.Runtime.dll"),
                new XElement("Private", "false")));
        return true;
    }

    private static bool ApplyMissingRuntimeDefine(XDocument document, MigrationOperation operation)
    {
        string? configuration = operation.Configuration;
        if (configuration is null)
        {
            return false;
        }

        string? requiredSymbol = GetCanonicalRuntimeSymbol(configuration) ??
                                 GetCanonicalRuntimeSymbolFromEvidence(operation.Evidence);
        if (requiredSymbol is null)
        {
            return false;
        }

        XElement[] propertyGroups = GetConfigurationPropertyGroups(document, configuration).ToArray();
        if (propertyGroups.Length == 0)
        {
            propertyGroups = [GetOrCreateConfigurationPropertyGroup(document, configuration)];
        }

        bool changed = false;
        foreach (XElement propertyGroup in propertyGroups)
        {
            XElement? defineConstants = propertyGroup.Elements().FirstOrDefault(IsNamed("DefineConstants"));
            if (defineConstants is null)
            {
                propertyGroup.Add(new XElement("DefineConstants", $"$(DefineConstants);{requiredSymbol}"));
                changed = true;
                continue;
            }

            List<string> defines = SplitMsBuildList(defineConstants.Value).ToList();
            if (defines.Any(define => string.Equals(define, requiredSymbol, StringComparison.Ordinal)))
            {
                continue;
            }

            defines.Add(requiredSymbol);
            defineConstants.Value = string.Join(";", defines);
            changed = true;
        }

        return changed;
    }

    private static string? GetCanonicalRuntimeSymbolFromEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return null;
        }

        if (Regex.IsMatch(evidence, @"\b(?:uses|requires?)\s+IL2CPP\b|\bIL2CPP\s+guards?\b", RegexOptions.IgnoreCase))
        {
            return "IL2CPP";
        }

        if (Regex.IsMatch(evidence, @"\b(?:uses|requires?)\s+MONO\b|\bMONO\s+guards?\b", RegexOptions.IgnoreCase))
        {
            return "MONO";
        }

        return null;
    }

    private static bool ApplySdkFacade(
        ProjectMigrationPlan projectPlan,
        MigrationOperation operation,
        XDocument document,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        var generator = new SdkFacadeGenerator();
        ProjectAnalysis project = AnalyzeProject(projectPlan.ProjectPath);
        SdkFacadePlan plan = generator.Plan(
            project,
            new SdkFacadeGeneratorOptions(FullSdk: operation.RuleId.Equals("generate_full_sdk_facade", StringComparison.OrdinalIgnoreCase)));
        if (!plan.HasContent)
        {
            return false;
        }

        bool changed = false;
        string source = generator.GenerateSource(plan);
        if (!File.Exists(plan.OutputPath) || !string.Equals(File.ReadAllText(plan.OutputPath), source, StringComparison.Ordinal))
        {
            TrackFile(plan.OutputPath, backupRoot, fileChanges);
            Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputPath)!);
            File.WriteAllText(plan.OutputPath, source, Encoding.UTF8);
            UpdateTrackedFileHash(plan.OutputPath, fileChanges);
            changed = true;
        }

        return EnsureGeneratedFacadeCompileInclude(document, projectPlan.ProjectPath, plan.OutputPath) || changed;
    }

    private static bool EnsureGeneratedFacadeCompileInclude(
        XDocument document,
        string projectPath,
        string facadePath)
    {
        if (!HasDefaultCompileItemsDisabled(document))
        {
            return false;
        }

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string includePath = Path.GetRelativePath(projectDirectory, facadePath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (document.Descendants()
            .Where(IsNamed("Compile"))
            .Any(element => string.Equals(element.Attribute("Include")?.Value, includePath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        XElement? itemGroup = document.Root!.Elements()
            .Where(IsNamed("ItemGroup"))
            .Where(group => group.Attribute("Condition") is null)
            .FirstOrDefault(group => group.Elements().Any(IsNamed("Compile")));
        if (itemGroup is null)
        {
            itemGroup = new XElement("ItemGroup");
            document.Root!.Add(itemGroup);
        }

        itemGroup.Add(new XElement("Compile", new XAttribute("Include", includePath)));
        return true;
    }

    private static bool HasDefaultCompileItemsDisabled(XDocument document) =>
        document.Descendants()
            .Where(IsNamed("EnableDefaultCompileItems"))
            .Any(element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    private static bool ApplySourceRiskReport(
        ProjectMigrationPlan projectPlan,
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!SourceRiskReportGenerator.HasSourceRiskOperations(projectPlan))
        {
            return false;
        }

        string report = new SourceRiskReportGenerator().GenerateReport(projectPlan);
        if (File.Exists(operation.FilePath) && string.Equals(File.ReadAllText(operation.FilePath), report, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        Directory.CreateDirectory(Path.GetDirectoryName(operation.FilePath)!);
        File.WriteAllText(operation.FilePath, report, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyUnityEventBridge(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        string source = new UnityEventBridgeGenerator().GenerateSource();
        if (File.Exists(operation.FilePath) && string.Equals(File.ReadAllText(operation.FilePath), source, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        Directory.CreateDirectory(Path.GetDirectoryName(operation.FilePath)!);
        File.WriteAllText(operation.FilePath, source, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyDelegateEventBridge(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        string source = new DelegateEventBridgeGenerator().GenerateSource();
        if (File.Exists(operation.FilePath) && string.Equals(File.ReadAllText(operation.FilePath), source, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        Directory.CreateDirectory(Path.GetDirectoryName(operation.FilePath)!);
        File.WriteAllText(operation.FilePath, source, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyBackendNeutralStarter(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        string source = new BackendNeutralStarterGenerator().GenerateSource();
        if (File.Exists(operation.FilePath) && string.Equals(File.ReadAllText(operation.FilePath), source, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        Directory.CreateDirectory(Path.GetDirectoryName(operation.FilePath)!);
        File.WriteAllText(operation.FilePath, source, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyHarmonyMethodTargets(
        ProjectMigrationPlan projectPlan,
        XDocument document,
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        IReadOnlyList<HarmonyMethodTarget> targets = new HarmonyMethodTargetCatalog().Discover(projectPlan.ProjectPath);
        if (targets.Count == 0)
        {
            return false;
        }

        string source = new HarmonyMethodTargetGenerator().GenerateSource(targets);
        bool changed = false;
        if (!File.Exists(operation.FilePath) || !string.Equals(File.ReadAllText(operation.FilePath), source, StringComparison.Ordinal))
        {
            TrackFile(operation.FilePath, backupRoot, fileChanges);
            Directory.CreateDirectory(Path.GetDirectoryName(operation.FilePath)!);
            File.WriteAllText(operation.FilePath, source, Encoding.UTF8);
            UpdateTrackedFileHash(operation.FilePath, fileChanges);
            changed = true;
        }

        return EnsureGeneratedFacadeCompileInclude(document, projectPlan.ProjectPath, operation.FilePath) || changed;
    }

    private static bool ApplyMemberAccessTargets(
        ProjectMigrationPlan projectPlan,
        XDocument document,
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        IReadOnlyList<MemberAccessTarget> targets = new MemberAccessTargetCatalog().Discover(projectPlan.ProjectPath);
        if (targets.Count == 0)
        {
            return false;
        }

        string source = new MemberAccessTargetGenerator().GenerateSource(targets);
        bool changed = false;
        if (!File.Exists(operation.FilePath) || !string.Equals(File.ReadAllText(operation.FilePath), source, StringComparison.Ordinal))
        {
            TrackFile(operation.FilePath, backupRoot, fileChanges);
            Directory.CreateDirectory(Path.GetDirectoryName(operation.FilePath)!);
            File.WriteAllText(operation.FilePath, source, Encoding.UTF8);
            UpdateTrackedFileHash(operation.FilePath, fileChanges);
            changed = true;
        }

        return EnsureGeneratedFacadeCompileInclude(document, projectPlan.ProjectPath, operation.FilePath) || changed;
    }

    private static bool ApplyUnityEventListenerRewrite(
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = new UnityEventListenerRewriter().RewriteSource(original);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyDelegateAssignmentRewrite(
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = new DelegateAssignmentRewriter().RewriteSource(original);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyDelegateArgumentRewrite(
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = new DelegateArgumentRewriter().RewriteSource(original);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyHarmonyOverloadBindingRewrite(
        string projectPath,
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        IReadOnlyList<HarmonyMethodTarget> targets = new HarmonyMethodTargetCatalog().Discover(projectPath);
        if (targets.Count == 0)
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = new HarmonyOverloadBindingRewriter().RewriteSource(original, sourcePath, targets);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyMemberAccessFallbackRewrite(
        string projectPath,
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        IReadOnlyList<MemberAccessTarget> targets = new MemberAccessTargetCatalog().Discover(projectPath);
        string rewritten = new MemberAccessFallbackRewriter().RewriteSource(original, sourcePath, targets);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyDirectMemberReflectionLookupRewrite(
        string projectPath,
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        IReadOnlyList<MemberAccessTarget> targets = new MemberAccessTargetCatalog().Discover(projectPath);
        if (targets.Count == 0)
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = new DirectMemberReflectionLookupRewriter().RewriteSource(original, sourcePath, targets);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyIl2CppObjectCastRewrite(
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = new Il2CppObjectCastRewriter().RewriteSource(original);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static ProjectAnalysis AnalyzeProject(string projectPath)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        WorkspaceAnalysis analysis = new WorkspaceAnalyzer().Analyze(projectPath);
        return analysis.Projects.FirstOrDefault(project =>
                   string.Equals(Path.GetFullPath(project.ProjectPath), fullProjectPath, StringComparison.OrdinalIgnoreCase)) ??
               new ProjectAnalysis(projectPath, Array.Empty<ConfigurationAnalysis>(), Array.Empty<InteropDiagnostic>());
    }

    private static bool ApplyBuildValidationHook(
        string projectPath,
        XDocument document,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        bool changed = false;
        string targetsPath = BuildValidationHook.GetTargetsPath(projectPath);
        string localPropsPath = BuildValidationHook.GetLocalPropsPath(projectPath);
        string gitIgnorePath = Path.Combine(Path.GetDirectoryName(projectPath)!, ".gitignore");
        string targetsSource = BuildValidationHook.GenerateTargets();
        string localPropsSource = BuildValidationHook.GenerateLocalProps();

        if (!File.Exists(targetsPath) || !string.Equals(File.ReadAllText(targetsPath), targetsSource, StringComparison.Ordinal))
        {
            TrackFile(targetsPath, backupRoot, fileChanges);
            File.WriteAllText(targetsPath, targetsSource, Encoding.UTF8);
            UpdateTrackedFileHash(targetsPath, fileChanges);
            changed = true;
        }

        if (!File.Exists(localPropsPath) || !string.Equals(File.ReadAllText(localPropsPath), localPropsSource, StringComparison.Ordinal))
        {
            TrackFile(localPropsPath, backupRoot, fileChanges);
            File.WriteAllText(localPropsPath, localPropsSource, Encoding.UTF8);
            UpdateTrackedFileHash(localPropsPath, fileChanges);
            changed = true;
        }

        if (EnsureGitIgnoreEntry(gitIgnorePath, BuildValidationHook.LocalPropsFileName, backupRoot, fileChanges))
        {
            changed = true;
        }

        if (BuildValidationHook.EnsureImport(document))
        {
            TrackExistingFile(projectPath, backupRoot, fileChanges);
            document.Save(projectPath);
            UpdateTrackedFileHash(projectPath, fileChanges);
            changed = true;
        }

        return changed;
    }

    private static bool ApplyScheduleOneUsingConditionalization(
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = ScheduleOneUsingRewriter.RewriteSource(
            original,
            ScheduleOneUsingRewriter.RewriteMode.PreferGlobalFacade);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyFullyQualifiedScheduleOneTypeRewrite(
        string projectPath,
        string sourcePath,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        var generator = new SdkFacadeGenerator();
        ProjectAnalysis project = AnalyzeProject(projectPath);
        SdkFacadePlan plan = generator.Plan(project);
        if (plan.TypeAliases.Count == 0)
        {
            return false;
        }

        string original = File.ReadAllText(sourcePath);
        string rewritten = SdkTypeAliasRewriter.RewriteSource(original, plan.TypeAliases);
        if (string.Equals(original, rewritten, StringComparison.Ordinal))
        {
            return false;
        }

        TrackFile(sourcePath, backupRoot, fileChanges);
        File.WriteAllText(sourcePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(sourcePath, fileChanges);
        return true;
    }

    private static bool ApplyInjectedTypeIntPtrConstructor(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(operation.FilePath))
        {
            return false;
        }

        SourceClassTarget target = ParseSourceClassTarget(operation);
        if (target.TypeName is null)
        {
            return false;
        }

        string original = File.ReadAllText(operation.FilePath);
        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = original.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  original.EndsWith('\n');
        List<string> lines = File.ReadAllLines(operation.FilePath).ToList();

        int classLine = FindTargetClassLine(lines, target);
        if (classLine < 0 ||
            !TryReadClassDeclaration(lines[classLine], out string classIndent, out string baseType) ||
            !IsIl2CppInjectedBase(baseType) ||
            HasIntPtrConstructor(lines, classLine, target.TypeName))
        {
            return false;
        }

        int openingBraceLine = FindOpeningBraceLine(lines, classLine);
        if (openingBraceLine < 0)
        {
            return false;
        }

        string memberIndent = GetMemberIndent(lines, openingBraceLine, classIndent);
        List<string> constructorLines =
        [
            $"{memberIndent}#if IL2CPP",
            $"{memberIndent}public {target.TypeName}(System.IntPtr ptr) : base(ptr) {{ }}"
        ];

        if (!HasParameterlessConstructor(lines, classLine, target.TypeName))
        {
            constructorLines.Add(string.Empty);
            constructorLines.Add(
                $"{memberIndent}public {target.TypeName}() : base(Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<{target.TypeName}>())");
            constructorLines.Add($"{memberIndent}{{");
            constructorLines.Add($"{memberIndent}    Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);");
            constructorLines.Add($"{memberIndent}}}");
        }

        constructorLines.Add($"{memberIndent}#endif");
        lines.InsertRange(openingBraceLine + 1, constructorLines);

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        string rewritten = string.Join(newline, lines);
        if (hadTrailingNewline)
        {
            rewritten += newline;
        }

        File.WriteAllText(operation.FilePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyInjectedTypeRegistration(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(operation.FilePath))
        {
            return false;
        }

        SourceClassTarget target = ParseSourceClassTarget(operation);
        if (target.TypeName is null)
        {
            return false;
        }

        string original = File.ReadAllText(operation.FilePath);
        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = original.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  original.EndsWith('\n');
        List<string> lines = File.ReadAllLines(operation.FilePath).ToList();

        int classLine = FindTargetClassLine(lines, target);
        if (classLine < 0 ||
            !TryReadClassDeclaration(lines[classLine], out string classIndent, out string baseType) ||
            !IsIl2CppInjectedBase(baseType) ||
            HasRegisterTypeAttribute(lines, classLine))
        {
            return false;
        }

        lines.InsertRange(classLine,
        [
            $"{classIndent}#if IL2CPP",
            $"{classIndent}[MelonLoader.RegisterTypeInIl2Cpp]",
            $"{classIndent}#endif"
        ]);

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        string rewritten = string.Join(newline, lines);
        if (hadTrailingNewline)
        {
            rewritten += newline;
        }

        File.WriteAllText(operation.FilePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyHideFromIl2CppAttribute(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(operation.FilePath))
        {
            return false;
        }

        SourceMemberTarget target = ParseSourceMemberTarget(operation);
        string original = File.ReadAllText(operation.FilePath);
        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = original.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  original.EndsWith('\n');
        List<string> lines = File.ReadAllLines(operation.FilePath).ToList();

        int signatureLine = FindTargetMemberLine(lines, target);
        if (signatureLine < 0 || HasHideFromIl2CppAttribute(lines, signatureLine))
        {
            return false;
        }

        string indent = Regex.Match(lines[signatureLine], @"^\s*").Value;
        string guard = lines.Any(line => line.Contains("#if !MONO", StringComparison.Ordinal))
            ? "!MONO"
            : "IL2CPP";

        lines.InsertRange(signatureLine,
        [
            $"{indent}#if {guard}",
            $"{indent}[Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]",
            $"{indent}#endif"
        ]);

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        string rewritten = string.Join(newline, lines);
        if (hadTrailingNewline)
        {
            rewritten += newline;
        }

        File.WriteAllText(operation.FilePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    private static bool ApplyGameConstructorSignature(
        MigrationOperation operation,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(operation.FilePath))
        {
            return false;
        }

        string original = File.ReadAllText(operation.FilePath);
        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hadTrailingNewline = original.EndsWith("\r\n", StringComparison.Ordinal) ||
                                  original.EndsWith('\n');
        List<string> lines = File.ReadAllLines(operation.FilePath).ToList();
        bool changed = ConditionalizeGameConstructorSignatures(lines);
        changed = ConditionalizeGameConstructorFactoryHelpers(lines) || changed;
        if (!changed)
        {
            return false;
        }

        TrackFile(operation.FilePath, backupRoot, fileChanges);
        string rewritten = string.Join(newline, lines);
        if (hadTrailingNewline)
        {
            rewritten += newline;
        }

        File.WriteAllText(operation.FilePath, rewritten, Encoding.UTF8);
        UpdateTrackedFileHash(operation.FilePath, fileChanges);
        return true;
    }

    public MigrationRollbackResult Rollback(string manifestPath)
    {
        MigrationApplyResult manifest = JsonSerializer.Deserialize(
            File.ReadAllText(manifestPath),
            S1InteropJsonContext.Default.MigrationApplyResult)
            ?? throw new InvalidOperationException("Could not read migration manifest.");

        var restored = new List<string>();
        var removed = new List<string>();

        foreach (MigrationFileChange change in manifest.FileChanges.AsEnumerable().Reverse())
        {
            if (change.Created)
            {
                if (File.Exists(change.FilePath))
                {
                    VerifyCurrentHash(change);
                    File.Delete(change.FilePath);
                    removed.Add(change.FilePath);
                }

                continue;
            }

            if (!File.Exists(change.BackupPath))
            {
                throw new FileNotFoundException($"Backup file is missing for rollback: {change.BackupPath}");
            }

            VerifyCurrentHash(change);
            File.Copy(change.BackupPath, change.FilePath, overwrite: true);
            restored.Add(change.FilePath);
        }

        return new MigrationRollbackResult(manifest.RunId, restored, removed);
    }

    private static void VerifyCurrentHash(MigrationFileChange change)
    {
        if (change.NewHash is null || !File.Exists(change.FilePath))
        {
            return;
        }

        string currentHash = FileHash.ComputeSha256(change.FilePath);
        if (!string.Equals(currentHash, change.NewHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing rollback because {change.FilePath} changed after migration.");
        }
    }

    private static IEnumerable<XElement> GetReferencesForConfiguration(XDocument document, string configuration)
    {
        foreach (XElement itemGroup in document.Descendants().Where(IsNamed("ItemGroup")))
        {
            string? itemGroupCondition = itemGroup.Attribute("Condition")?.Value;
            if (!ConditionApplies(itemGroupCondition, configuration))
            {
                continue;
            }

            foreach (XElement reference in itemGroup.Elements().Where(IsNamed("Reference")))
            {
                string? referenceCondition = reference.Attribute("Condition")?.Value;
                if (ConditionApplies(referenceCondition, configuration))
                {
                    yield return reference;
                }
            }
        }
    }

    private static XElement GetOrCreateConfigurationPropertyGroup(XDocument document, string configuration)
    {
        XElement? existing = GetConfigurationPropertyGroups(document, configuration).FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        XElement created = new("PropertyGroup", new XAttribute("Condition", $"'$(Configuration)'=='{configuration}'"));
        document.Root!.Add(created);
        return created;
    }

    private static IEnumerable<XElement> GetConfigurationPropertyGroups(XDocument document, string configuration) =>
        document.Descendants()
            .Where(IsNamed("PropertyGroup"))
            .Where(element => ConditionMentionsConfiguration(element.Attribute("Condition")?.Value, configuration));

    private static XElement GetOrCreateConfigurationItemGroup(XDocument document, string configuration)
    {
        XElement? existing = document.Descendants()
            .Where(IsNamed("ItemGroup"))
            .FirstOrDefault(element => ConditionMentionsConfiguration(element.Attribute("Condition")?.Value, configuration));
        if (existing is not null)
        {
            return existing;
        }

        XElement created = new("ItemGroup", new XAttribute("Condition", $"'$(Configuration)'=='{configuration}'"));
        document.Root!.Add(created);
        return created;
    }

    private static void EnsureLocalBuildPropsImport(XDocument document)
    {
        bool exists = document.Root!.Elements().Where(IsNamed("Import")).Any(import =>
            string.Equals(import.Attribute("Project")?.Value, "local.build.props", StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return;
        }

        document.Root!.AddFirst(new XElement(
            "Import",
            new XAttribute("Project", "local.build.props"),
            new XAttribute("Condition", "Exists('local.build.props')")));
    }

    private static void WriteLocalProps(
        string path,
        IReadOnlyDictionary<string, string> properties,
        bool includeValues,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        TrackFile(path, backupRoot, fileChanges);

        var document = new XDocument(
            new XElement("Project",
                new XElement("PropertyGroup",
                    properties.Select(property =>
                        new XElement(property.Key, includeValues ? property.Value : string.Empty)))));
        document.Save(path);
        UpdateTrackedFileHash(path, fileChanges);
    }

    private static bool EnsureGitIgnoreEntry(
        string path,
        string entry,
        string backupRoot,
        List<MigrationFileChange> fileChanges)
    {
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        if (lines.Any(line => string.Equals(line.Trim(), entry, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        TrackFile(path, backupRoot, fileChanges);
        lines.Add(entry);
        File.WriteAllLines(path, lines, Encoding.UTF8);
        UpdateTrackedFileHash(path, fileChanges);
        return true;
    }

    private static void TrackExistingFile(string path, string backupRoot, List<MigrationFileChange> fileChanges)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Cannot migrate missing file: {path}");
        }

        TrackFile(path, backupRoot, fileChanges);
    }

    private static void TrackFile(string path, string backupRoot, List<MigrationFileChange> fileChanges)
    {
        if (fileChanges.Any(change => string.Equals(change.FilePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bool exists = File.Exists(path);
        string backupPath = Path.Combine(backupRoot, $"{fileChanges.Count:0000}-{Path.GetFileName(path)}.s1interop-backup");
        string? originalHash = exists ? FileHash.ComputeSha256(path) : null;
        if (exists)
        {
            File.Copy(path, backupPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(backupPath, string.Empty, Encoding.UTF8);
        }

        fileChanges.Add(new MigrationFileChange(path, backupPath, originalHash, null, Created: !exists));
    }

    private static void UpdateTrackedFileHash(string path, List<MigrationFileChange> fileChanges)
    {
        int index = fileChanges.FindIndex(change => string.Equals(change.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || !File.Exists(path))
        {
            return;
        }

        MigrationFileChange current = fileChanges[index];
        fileChanges[index] = current with { NewHash = FileHash.ComputeSha256(path) };
    }

    private static string GetRunBasePath(string rootPath)
    {
        string fullPath = Path.GetFullPath(rootPath);
        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath)!;
        }

        return Directory.Exists(fullPath) ? fullPath : Directory.GetCurrentDirectory();
    }

    private static bool ConditionApplies(string? condition, string configuration)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        MatchCollection matches = ConfigurationConditionRegex.Matches(condition);
        if (matches.Count == 0)
        {
            return true;
        }

        return matches.Any(match =>
            string.Equals(match.Groups["name"].Value, configuration, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ConditionMentionsConfiguration(string? condition, string configuration)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        return ExtractConfigurationNames(condition).Any(name =>
            string.Equals(name, configuration, StringComparison.OrdinalIgnoreCase));
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
                match.Groups["right"].Value);
            if (configurationName is not null)
            {
                yield return configurationName;
            }
        }

        foreach (Match match in ConfigurationConditionRegex.Matches(condition))
        {
            string configurationName = match.Groups["name"].Value;
            if (IsValidConfigurationName(configurationName))
            {
                yield return configurationName;
            }
        }
    }

    private static string? ExtractConfigurationComparisonValue(string left, string right)
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
        return IsValidConfigurationName(configurationName) ? configurationName : null;
    }

    private static bool IsValidConfigurationName(string configurationName) =>
        !string.IsNullOrWhiteSpace(configurationName) &&
        !configurationName.Equals("=", StringComparison.Ordinal) &&
        !configurationName.Contains("$(", StringComparison.Ordinal);

    private static bool IsGameReference(XElement reference)
    {
        string include = reference.Attribute("Include")?.Value ?? string.Empty;
        string hintPath = reference.Elements().FirstOrDefault(IsNamed("HintPath"))?.Value ?? string.Empty;
        string text = $"{include}|{hintPath}";
        return text.Contains("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ScheduleOne", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Unity.", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("FishNet", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("MelonLoader", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(ManagedPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(MonoAssembliesPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(Il2CppAssembliesPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(MelonLoaderPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(MelonLoaderAssembliesPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(LocalMonoDeploymentPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("$(LocalIl2CppDeploymentPath)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("0Harmony", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Il2CppInterop", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("SteamNetworkLib", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("S1API", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("S1MAPI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("bGUI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("MeshVault", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAbsoluteWindowsPath(string value) =>
        value.Length >= 3 &&
        char.IsLetter(value[0]) &&
        value[1] == ':' &&
        (value[2] == '\\' || value[2] == '/');

    private static bool TryGetGameRoot(string value, out string gameRoot)
    {
        foreach (string marker in new[] { @"\MelonLoader\", @"\Schedule I_Data\", @"\UserLibs\", @"\Mods\", "/MelonLoader/", "/Schedule I_Data/", "/UserLibs/", "/Mods/" })
        {
            int markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                gameRoot = value[..markerIndex];
                return LooksLikeGameRoot(gameRoot);
            }
        }

        gameRoot = string.Empty;
        return false;
    }

    private static bool LooksLikeGameRoot(string value)
    {
        string normalized = value.TrimEnd('\\', '/');
        return IsAbsoluteWindowsPath(normalized) &&
               normalized.Contains("Schedule I", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIl2CppConfiguration(string configuration) =>
        configuration.Contains("il2cpp", StringComparison.OrdinalIgnoreCase) ||
        configuration.Contains("cpp", StringComparison.OrdinalIgnoreCase);

    private static string GetTargetFrameworkForOperation(MigrationOperation operation)
    {
        if (operation.Configuration is not null && IsIl2CppConfiguration(operation.Configuration))
        {
            return "net6.0";
        }

        if (operation.Evidence?.Contains("Runtime=Il2Cpp", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "net6.0";
        }

        if (operation.Evidence?.Contains("Runtime=Mono", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "netstandard2.1";
        }

        return "netstandard2.1";
    }

    private static SourceMemberTarget ParseSourceMemberTarget(MigrationOperation operation)
    {
        if (operation.Evidence is null)
        {
            return new SourceMemberTarget(null, null, null);
        }

        Match match = SourceDiagnosticEvidenceRegex.Match(operation.Evidence);
        if (!match.Success)
        {
            return new SourceMemberTarget(null, null, null);
        }

        return new SourceMemberTarget(
            int.TryParse(match.Groups["line"].Value, out int line) ? line : null,
            match.Groups["method"].Value,
            match.Groups["params"].Success ? match.Groups["params"].Value : null);
    }

    private static SourceClassTarget ParseSourceClassTarget(MigrationOperation operation)
    {
        if (operation.Evidence is null)
        {
            return new SourceClassTarget(null, null);
        }

        Match match = SourceClassDiagnosticEvidenceRegex.Match(operation.Evidence);
        if (!match.Success)
        {
            return new SourceClassTarget(null, null);
        }

        return new SourceClassTarget(
            int.TryParse(match.Groups["line"].Value, out int line) ? line : null,
            match.Groups["type"].Value);
    }

    private static int FindTargetClassLine(IReadOnlyList<string> lines, SourceClassTarget target)
    {
        if (target.Line is int lineNumber)
        {
            int index = lineNumber - 1;
            if (index >= 0 && index < lines.Count && IsTargetClassLine(lines[index], target.TypeName))
            {
                return index;
            }

            int nearby = FindTargetClassLineInRange(lines, target, Math.Max(0, index - 8), Math.Min(lines.Count - 1, index + 8));
            if (nearby >= 0)
            {
                return nearby;
            }
        }

        return FindTargetClassLineInRange(lines, target, 0, lines.Count - 1);
    }

    private static int FindTargetClassLineInRange(
        IReadOnlyList<string> lines,
        SourceClassTarget target,
        int start,
        int end)
    {
        for (int index = start; index <= end; index++)
        {
            if (IsTargetClassLine(lines[index], target.TypeName))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsTargetClassLine(string line, string? typeName)
    {
        if (typeName is null)
        {
            return false;
        }

        Match match = ClassDeclarationRegex.Match(line);
        return match.Success &&
               string.Equals(match.Groups["name"].Value, typeName, StringComparison.Ordinal);
    }

    private static bool TryReadClassDeclaration(string line, out string indent, out string baseType)
    {
        Match match = ClassDeclarationRegex.Match(line);
        if (!match.Success)
        {
            indent = string.Empty;
            baseType = string.Empty;
            return false;
        }

        indent = match.Groups["indent"].Value;
        baseType = match.Groups["base"].Success ? match.Groups["base"].Value.Trim() : string.Empty;
        return true;
    }

    private static bool IsIl2CppInjectedBase(string baseType)
    {
        string[] baseTypes = baseType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string candidate in baseTypes)
        {
            string simpleName = candidate.Split('.').Last();
            if (simpleName is "MonoBehaviour" or
                "InteractableObject" or
                "Equippable" or
                "VehicleData")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConditionalizeGameConstructorSignatures(List<string> lines)
    {
        bool changed = false;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!TryReadClassDeclaration(lines[index], out string classIndent, out string baseType) ||
                !IsLikelyGameBackedBase(baseType))
            {
                continue;
            }

            Match classMatch = ClassDeclarationRegex.Match(lines[index]);
            if (!classMatch.Success)
            {
                continue;
            }

            string typeName = classMatch.Groups["name"].Value;
            int classEndLine = FindClassEndLine(lines, index);
            for (int memberIndex = index + 1; memberIndex <= classEndLine && memberIndex < lines.Count; memberIndex++)
            {
                string line = lines[memberIndex];
                if (!line.Contains($" {typeName}(", StringComparison.Ordinal) ||
                    !line.Contains("Guid", StringComparison.Ordinal) ||
                    !line.Contains("List<", StringComparison.Ordinal) ||
                    IsAlreadyRuntimeGuarded(lines, memberIndex))
                {
                    continue;
                }

                int baseLine = memberIndex + 1 < lines.Count && lines[memberIndex + 1].Contains(": base(", StringComparison.Ordinal)
                    ? memberIndex + 1
                    : -1;
                if (baseLine < 0)
                {
                    continue;
                }

                string indent = GetIndent(line);
                string il2CppLine = ReplaceGameConstructorIl2CppSignatureTypes(line);
                string baseLineText = lines[baseLine];
                lines.RemoveAt(baseLine);
                lines.RemoveAt(memberIndex);
                lines.InsertRange(memberIndex,
                [
                    $"{indent}#if IL2CPP",
                    il2CppLine,
                    baseLineText,
                    $"{indent}#else",
                    line,
                    baseLineText,
                    $"{indent}#endif"
                ]);
                changed = true;
                break;
            }
        }

        return changed;
    }

    private static bool ConditionalizeGameConstructorFactoryHelpers(List<string> lines)
    {
        bool changed = false;
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            if (line.Contains("?? new List<", StringComparison.Ordinal) &&
                !IsAlreadyRuntimeGuarded(lines, index))
            {
                string indent = GetIndent(line);
                lines.RemoveAt(index);
                lines.InsertRange(index,
                [
                    $"{indent}#if IL2CPP",
                    line.Replace("new List<", "new Il2CppSystem.Collections.Generic.List<", StringComparison.Ordinal),
                    $"{indent}#else",
                    line,
                    $"{indent}#endif"
                ]);
                changed = true;
                index += 4;
                continue;
            }

            if (line.Contains("new Guid(", StringComparison.Ordinal) &&
                !line.Contains("Il2CppSystem.Guid", StringComparison.Ordinal) &&
                !IsAlreadyRuntimeGuarded(lines, index))
            {
                string indent = GetIndent(line);
                lines.RemoveAt(index);
                lines.InsertRange(index,
                [
                    $"{indent}#if IL2CPP",
                    line.Replace("new Guid(", "new Il2CppSystem.Guid(", StringComparison.Ordinal),
                    $"{indent}#else",
                    line,
                    $"{indent}#endif"
                ]);
                changed = true;
                index += 4;
            }
        }

        return changed;
    }

    private static string ReplaceGameConstructorIl2CppSignatureTypes(string line) =>
        line.Replace("Guid ", "Il2CppSystem.Guid ", StringComparison.Ordinal)
            .Replace("List<", "Il2CppSystem.Collections.Generic.List<", StringComparison.Ordinal);

    private static bool IsLikelyGameBackedBase(string baseType) =>
        baseType
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => IsIl2CppInjectedBase(candidate) ||
                              IsKnownGameDataBase(candidate) ||
                              candidate.Contains("ScheduleOne.", StringComparison.Ordinal) ||
                              candidate.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal));

    private static bool IsKnownGameDataBase(string candidate)
    {
        string simpleName = candidate.Split('.').Last();
        return simpleName is "VehicleData" or "GameData";
    }

    private static bool IsAlreadyRuntimeGuarded(IReadOnlyList<string> lines, int lineIndex)
    {
        var runtimeGuardStack = new Stack<bool>();
        for (int index = 0; index <= lineIndex && index < lines.Count; index++)
        {
            string trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
            {
                runtimeGuardStack.Push(IsRuntimeGuardDirective(trimmed));
                continue;
            }

            if (trimmed.StartsWith("#elif ", StringComparison.Ordinal))
            {
                bool previous = runtimeGuardStack.Count > 0 && runtimeGuardStack.Pop();
                runtimeGuardStack.Push(previous || IsRuntimeGuardDirective(trimmed));
                continue;
            }

            if (trimmed.StartsWith("#endif", StringComparison.Ordinal) && runtimeGuardStack.Count > 0)
            {
                runtimeGuardStack.Pop();
            }
        }

        return runtimeGuardStack.Contains(true);
    }

    private static bool IsRuntimeGuardDirective(string directive) =>
        directive.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase) ||
        directive.Contains("MONO", StringComparison.OrdinalIgnoreCase);

    private static string GetIndent(string line) => line[..^line.TrimStart().Length];

    private static bool HasRegisterTypeAttribute(IReadOnlyList<string> lines, int classLine)
    {
        for (int index = Math.Max(0, classLine - 8); index < classLine; index++)
        {
            if (lines[index].Contains("RegisterTypeInIl2Cpp", StringComparison.Ordinal))
            {
                return true;
            }

            if (index < classLine - 1 && ClassDeclarationRegex.IsMatch(lines[index]))
            {
                return false;
            }
        }

        return false;
    }

    private static bool HasIntPtrConstructor(IReadOnlyList<string> lines, int classLine, string typeName)
    {
        int endLine = FindClassEndLine(lines, classLine);
        if (endLine < 0)
        {
            return true;
        }

        Regex constructorRegex = new(
            $@"^\s*(?:public|protected|internal|private)\s+{Regex.Escape(typeName)}\s*\(\s*(?:System\.)?IntPtr\s+\w+\s*\)\s*:\s*base\s*\(\s*\w+\s*\)",
            RegexOptions.Compiled);
        for (int index = classLine + 1; index <= endLine; index++)
        {
            if (constructorRegex.IsMatch(lines[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasParameterlessConstructor(IReadOnlyList<string> lines, int classLine, string typeName)
    {
        int endLine = FindClassEndLine(lines, classLine);
        if (endLine < 0)
        {
            return true;
        }

        Regex constructorRegex = new(
            $@"^\s*(?:public|protected|internal|private)\s+{Regex.Escape(typeName)}\s*\(\s*\)",
            RegexOptions.Compiled);
        for (int index = classLine + 1; index <= endLine; index++)
        {
            if (constructorRegex.IsMatch(lines[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindOpeningBraceLine(IReadOnlyList<string> lines, int classLine)
    {
        for (int index = classLine; index < lines.Count; index++)
        {
            if (lines[index].Contains('{', StringComparison.Ordinal))
            {
                return index;
            }

            if (index > classLine && ClassDeclarationRegex.IsMatch(lines[index]))
            {
                return -1;
            }
        }

        return -1;
    }

    private static int FindClassEndLine(IReadOnlyList<string> lines, int classLine)
    {
        int openingBraceLine = FindOpeningBraceLine(lines, classLine);
        if (openingBraceLine < 0)
        {
            return -1;
        }

        int depth = 0;
        bool sawOpeningBrace = false;
        for (int index = openingBraceLine; index < lines.Count; index++)
        {
            foreach (char character in lines[index])
            {
                if (character == '{')
                {
                    depth++;
                    sawOpeningBrace = true;
                }
                else if (character == '}')
                {
                    depth--;
                    if (sawOpeningBrace && depth == 0)
                    {
                        return index;
                    }
                }
            }
        }

        return -1;
    }

    private static string GetMemberIndent(IReadOnlyList<string> lines, int openingBraceLine, string classIndent)
    {
        int endLine = FindClassEndLine(lines, openingBraceLine);
        if (endLine < 0)
        {
            return $"{classIndent}    ";
        }

        for (int index = openingBraceLine + 1; index < endLine; index++)
        {
            Match match = Regex.Match(lines[index], @"^(?<indent>\s+)(?:public|protected|internal|private)\s+");
            if (match.Success)
            {
                return match.Groups["indent"].Value;
            }
        }

        return $"{classIndent}    ";
    }

    private static int FindTargetMemberLine(IReadOnlyList<string> lines, SourceMemberTarget target)
    {
        if (target.Line is int lineNumber)
        {
            int index = lineNumber - 1;
            if (index >= 0 && index < lines.Count && IsTargetMemberLine(lines[index], target.MethodName, target.ParameterText))
            {
                return index;
            }

            int nearby = FindTargetMemberLineInRange(lines, target, Math.Max(0, index - 8), Math.Min(lines.Count - 1, index + 8));
            if (nearby >= 0)
            {
                return nearby;
            }
        }

        if (target.MethodName is null)
        {
            return -1;
        }

        return FindTargetMemberLineInRange(lines, target, 0, lines.Count - 1);
    }

    private static int FindTargetMemberLineInRange(
        IReadOnlyList<string> lines,
        SourceMemberTarget target,
        int start,
        int end)
    {
        for (int index = start; index <= end; index++)
        {
            if (IsTargetMemberLine(lines[index], target.MethodName, target.ParameterText))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsTargetMemberLine(string line, string? methodName, string? parameterText)
    {
        if (!line.Contains("public ", StringComparison.Ordinal))
        {
            return false;
        }

        if (methodName is not null &&
            !Regex.IsMatch(line, $@"(?<![A-Za-z0-9_]){Regex.Escape(methodName)}\s*\("))
        {
            return false;
        }

        return parameterText is null || NormalizeParameterText(line).Contains(NormalizeParameterText(parameterText), StringComparison.Ordinal);
    }

    private static string NormalizeParameterText(string text) =>
        Regex.Replace(text, @"\s+", string.Empty);

    private static bool HasHideFromIl2CppAttribute(IReadOnlyList<string> lines, int memberLine)
    {
        for (int index = Math.Max(0, memberLine - 6); index < memberLine; index++)
        {
            if (lines[index].Contains("HideFromIl2Cpp", StringComparison.Ordinal))
            {
                return true;
            }

            if (lines[index].Contains("public ", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return false;
    }

    private static string? GetCanonicalRuntimeSymbol(string configuration)
    {
        if (IsIl2CppConfiguration(configuration))
        {
            return "IL2CPP";
        }

        if (configuration.Contains("mono", StringComparison.OrdinalIgnoreCase))
        {
            return "MONO";
        }

        return null;
    }

    private static IReadOnlyList<string> SplitMsBuildList(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Func<XElement, bool> IsNamed(string localName) =>
        element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private sealed record SourceMemberTarget(int? Line, string? MethodName, string? ParameterText);

    private sealed record SourceClassTarget(int? Line, string? TypeName);

    private sealed record AbsolutePropertyValue(
        XElement Element,
        string PropertyName,
        string Value,
        string? PropertyCondition,
        string? PropertyGroupCondition);
}
