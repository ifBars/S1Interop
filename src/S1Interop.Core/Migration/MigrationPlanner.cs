using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core;

public sealed class MigrationPlanner
{
    private static readonly Regex SourceDiagnosticEvidenceRegex = new(
        @"^(?<path>.+\.cs):(?<line>\d+):\s*(?<member>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)(?:\((?<params>[^)]*)\))?\s+uses\s+",
        RegexOptions.Compiled);

    public MigrationPlan Plan(WorkspaceAnalysis analysis) =>
        Plan(analysis, MigrationPlannerOptions.Default);

    public MigrationPlan Plan(WorkspaceAnalysis analysis, MigrationPlannerOptions options)
    {
        ProjectMigrationPlan[] projects = analysis.Projects
            .Select(project => new ProjectMigrationPlan(project.ProjectPath, PlanProject(project, options)))
            .ToArray();

        return new MigrationPlan(analysis.RootPath, projects);
    }

    private static IReadOnlyList<MigrationOperation> PlanProject(ProjectAnalysis project, MigrationPlannerOptions options)
    {
        var operations = new List<MigrationOperation>();

        foreach (InteropDiagnostic diagnostic in project.Diagnostics)
        {
            MigrationOperation? operation = diagnostic.RuleId switch
            {
                "wrong_target_framework" => CreateTargetFrameworkOperation(project, diagnostic),

                "wrong_il2cpp_reference_surface" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    diagnostic.Configuration,
                    "medium",
                    true,
                    "Replace IL2CPP Managed-folder references with MelonLoader-generated Il2CppAssemblies references when the target wrapper name is unambiguous."),

                "stale_publicized_surface" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    diagnostic.Configuration,
                    "medium",
                    true,
                    "Replace stale Assembly-CSharp-publicized references with current Assembly-CSharp references plus build-time publicization for Mono configurations."),

                "reference_should_not_copy_local" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    diagnostic.Configuration,
                    "low",
                    true,
                    "Set Private=false on game, Unity, MelonLoader, Harmony, and generated wrapper references so local assemblies are not copied into mod output."),

                "local_path_in_project" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    diagnostic.Configuration,
                    "medium",
                    true,
                    "Move committed absolute game paths into ignored local.build.props or command-line property defaults."),

                "global_usings_require_langversion" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    null,
                    "low",
                    true,
                    "Set LangVersion to 10.0 so generated global using facades compile on netstandard-era mod projects.",
                    diagnostic.Evidence),

                "missing_local_reference_properties" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    null,
                    "low",
                    true,
                    "Create ignored local.build.props scaffolding for unresolved modding reference path properties."),

                "missing_local_build_props_import" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    null,
                    "low",
                    true,
                    "Import generated local.build.props so local Mono/IL2CPP game path properties are visible to builds.",
                    diagnostic.Evidence),

                "missing_il2cppinterop_reference" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    diagnostic.Configuration,
                    "low",
                    true,
                    "Add an Il2CppInterop.Runtime reference from MelonLoader net6 for IL2CPP configurations that use generated wrappers."),

                "missing_runtime_define" => new MigrationOperation(
                    diagnostic.RuleId,
                    project.ProjectPath,
                    diagnostic.Configuration,
                    "low",
                    true,
                    "Add the canonical MONO or IL2CPP DefineConstants symbol required by source conditional compilation.",
                    diagnostic.Evidence),

                "injected_type_missing_registertype" => new MigrationOperation(
                    diagnostic.RuleId,
                    diagnostic.Evidence is null ? project.ProjectPath : GetDiagnosticFilePath(diagnostic.Evidence, project.ProjectPath),
                    diagnostic.Configuration,
                    "low",
                    true,
                    "Add a guarded RegisterTypeInIl2Cpp attribute to project-owned Unity component types used by IL2CPP.",
                    diagnostic.Evidence),

                "injected_type_missing_intptr_constructor" => new MigrationOperation(
                    diagnostic.RuleId,
                    diagnostic.Evidence is null ? project.ProjectPath : GetDiagnosticFilePath(diagnostic.Evidence, project.ProjectPath),
                    diagnostic.Configuration,
                    "low",
                    true,
                    "Add a guarded System.IntPtr constructor for safe MonoBehaviour injected types.",
                    diagnostic.Evidence),

                "injected_member_requires_hidefromil2cpp" => CreateHideFromIl2CppOperation(diagnostic),

                "game_constructor_requires_il2cpp_signature" => new MigrationOperation(
                    diagnostic.RuleId,
                    diagnostic.Evidence is null ? project.ProjectPath : GetDiagnosticFilePath(diagnostic.Evidence, project.ProjectPath),
                    null,
                    "low",
                    true,
                    "Conditionalize game-constructor signatures so IL2CPP builds use wrapper Guid/List types.",
                    diagnostic.Evidence),

                _ => null
            };

            if (operation is not null)
            {
                operations.Add(operation);
            }
        }

        if (options.DualRuntime && DualRuntimeProjectScaffolder.NeedsIl2CppConfigurations(project))
        {
            string monoConfigurations = string.Join(
                ";",
                project.Configurations
                    .Where(DualRuntimeProjectScaffolder.IsSourceMonoConfiguration)
                    .Select(configuration => configuration.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            operations.Add(new MigrationOperation(
                "add_il2cpp_configuration",
                project.ProjectPath,
                null,
                "medium",
                true,
                "Add IL2CPP build configurations mirrored from existing Mono configurations, including net6.0, IL2CPP defines, and generated-wrapper reference paths.",
                $"mono_configurations={monoConfigurations}"));
            if (S1InteropGeneratorDetector.ProjectUsesGeneratorAttributes(project.ProjectPath))
            {
                operations.Add(new MigrationOperation(
                    "install_s1interop_generator_package",
                    project.ProjectPath,
                    null,
                    "low",
                    true,
                    "Install the private S1Interop Roslyn generator package so backend-specific reflection and facade helpers can be generated at compile time."));
            }

            string[] scheduleOneUsingFiles = ScheduleOneUsingRewriter
                .FindFilesWithUnconditionalScheduleOneUsings(project.ProjectPath)
                .ToArray();
            if (scheduleOneUsingFiles.Length > 0)
            {
                AddGeneratedGuardRuntimeDefineOperations(project, operations);
            }

            foreach (string sourceFile in scheduleOneUsingFiles)
            {
                operations.Add(new MigrationOperation(
                    "conditionalize_scheduleone_usings",
                    sourceFile,
                    null,
                    "medium",
                    true,
                    "Move ordinary ScheduleOne namespace imports into the generated facade and guard alias/static ScheduleOne usings that must remain in source."));
            }
        }
        if (ProjectHasUnconditionedImportedMonoRuntimeFlag(project.ProjectPath) ||
            ProjectHasStaleMonoReferenceImport(project.ProjectPath))
        {
            operations.Add(new MigrationOperation(
                "condition_imported_mono_runtime_flags",
                project.ProjectPath,
                null,
                "low",
                true,
                "Condition project-local imported IsMono=true props on S1InteropTargetRuntime so IL2CPP configurations do not import Mono-only references."));
        }

        if (options.BuildHook)
        {
            operations.Add(new MigrationOperation(
                "install_build_validation_hook",
                BuildValidationHook.GetTargetsPath(project.ProjectPath),
                null,
                "low",
                true,
                "Install a project-local MSBuild target that runs S1Interop lint before compilation and can be restored through the migration manifest."));
        }

        SdkFacadePlan facadePlan = new SdkFacadeGenerator().Plan(project);
        if (facadePlan.HasContent)
        {
            if (facadePlan.TypeAliases.Count > 0 &&
                !operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"))
            {
                operations.Add(new MigrationOperation(
                    "install_s1interop_generator_package",
                    project.ProjectPath,
                    null,
                    "low",
                    true,
                    "Install the S1Interop Roslyn generator package required by generated SDK facade type registry attributes."));
            }

            operations.Add(new MigrationOperation(
                "generate_sdk_facade",
                facadePlan.OutputPath,
                null,
                "low",
                true,
                "Generate S1Interop global using facade for detected ScheduleOne namespaces so source can move away from manual Mono/IL2CPP using blocks."));

            foreach (string sourceFile in SdkTypeAliasRewriter.FindFilesWithRewritableTypeAliases(project.ProjectPath, facadePlan.TypeAliases))
            {
                operations.Add(new MigrationOperation(
                    "rewrite_fully_qualified_scheduleone_types",
                    sourceFile,
                    null,
                    "low",
                    true,
                    "Rewrite unique fully-qualified ScheduleOne type references to generated S1Interop type aliases."));
            }

            foreach (string sourceFile in SdkStringTypeLookupRewriter.FindFilesWithRewritableTypeLookups(project.ProjectPath, facadePlan.TypeAliases))
            {
                operations.Add(new MigrationOperation(
                    "rewrite_scheduleone_string_type_lookups",
                    sourceFile,
                    null,
                    "low",
                    true,
                    "Rewrite simple ScheduleOne string type lookups to generated backend-neutral type registry properties."));
            }
        }

        if (options.IncludeSourceRisks && project.SourceInterop is not null && project.SourceInterop.SourceRisks.Count > 0)
        {
            SourceRisk[] automaticUnityEventRisks = project.SourceInterop.SourceRisks
                .Where(UnityEventListenerRewriter.CanRewrite)
                .ToArray();
            SourceRisk[] automaticDelegateRisks = project.SourceInterop.SourceRisks
                .Where(DelegateAssignmentRewriter.CanRewrite)
                .ToArray();
            SourceRisk[] automaticDelegateArgumentRisks = project.SourceInterop.SourceRisks
                .Where(DelegateArgumentRewriter.CanRewrite)
                .ToArray();
            SourceRisk[] automaticHarmonyOverloadRisks = project.SourceInterop.SourceRisks
                .Where(HarmonyOverloadBindingRewriter.CanRewrite)
                .ToArray();
            SourceRisk[] automaticMemberAccessRisks = project.SourceInterop.SourceRisks
                .Where(MemberAccessFallbackRewriter.CanRewrite)
                .ToArray();
            SourceRisk[] automaticDirectMemberLookupRisks = project.SourceInterop.SourceRisks
                .Where(DirectMemberReflectionLookupRewriter.CanRewrite)
                .ToArray();
            SourceRisk[] automaticObjectCastRisks = project.SourceInterop.SourceRisks
                .Where(Il2CppObjectCastRewriter.CanRewrite)
                .ToArray();
            MemberAccessTarget[] generatedMemberAccessTargets = new MemberAccessTargetCatalog()
                .Discover(project.ProjectPath)
                .ToArray();
            int generatedMemberAccessTargetCount = generatedMemberAccessTargets.Length;
            bool hasGeneratedMemberAccessTargets = generatedMemberAccessTargetCount > 0;
            bool hasAutomaticMemberAccessRewrites = automaticMemberAccessRisks.Length > 0 || automaticDirectMemberLookupRisks.Length > 0;
            SourceRisk[] manualRisks = project.SourceInterop.SourceRisks
                .Except(automaticUnityEventRisks)
                .Except(automaticDelegateRisks)
                .Except(automaticDelegateArgumentRisks)
                .Except(automaticHarmonyOverloadRisks)
                .Except(automaticMemberAccessRisks)
                .Except(automaticDirectMemberLookupRisks)
                .Except(automaticObjectCastRisks)
                .ToArray();

            if (automaticUnityEventRisks.Length > 0)
            {
                operations.Add(new MigrationOperation(
                    "generate_unity_event_bridge",
                    UnityEventBridgeGenerator.GetSourcePath(project.ProjectPath),
                    null,
                    "low",
                    true,
                    "Generate a small UnityEvent bridge that converts simple listener registrations between Mono UnityAction and IL2CPP System.Action shapes."));

                foreach (string sourceFile in automaticUnityEventRisks
                             .Select(risk => risk.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_unity_event_listeners",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple direct UnityEvent AddListener/RemoveListener calls through the generated S1Interop UnityEvent bridge."));
                }
            }

            if (automaticDelegateRisks.Length > 0)
            {
                operations.Add(new MigrationOperation(
                    "generate_delegate_event_bridge",
                    DelegateEventBridgeGenerator.GetSourcePath(project.ProjectPath),
                    null,
                    "low",
                    true,
                    "Generate a small delegate-event bridge used by safe Delegate.Combine/Remove self-assignment rewrites."));

                foreach (string sourceFile in automaticDelegateRisks
                             .Select(risk => risk.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_delegate_assignments",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple Delegate.Combine/Remove self-assignments through the generated S1Interop delegate bridge."));
                }
            }

            if (automaticDelegateArgumentRisks.Length > 0)
            {
                if (!operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"))
                {
                    operations.Add(new MigrationOperation(
                        "install_s1interop_generator_package",
                        project.ProjectPath,
                        null,
                        "low",
                        true,
                        "Install the S1Interop Roslyn generator package required by generated backend-neutral delegate conversion helpers."));
                }

                foreach (string sourceFile in automaticDelegateArgumentRisks
                             .Select(risk => risk.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_delegate_arguments",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple direct Unity/game delegate arguments through the generated S1InteropDelegateBridge conversion helper."));
                }
            }

            if (automaticHarmonyOverloadRisks.Length > 0)
            {
                if (!operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"))
                {
                    operations.Add(new MigrationOperation(
                        "install_s1interop_generator_package",
                        project.ProjectPath,
                        null,
                        "low",
                        true,
                        "Install the S1Interop Roslyn generator package required by generated Harmony method target attributes."));
                }

                operations.Add(new MigrationOperation(
                    "generate_harmony_method_targets",
                    HarmonyMethodTargetGenerator.GetSourcePath(project.ProjectPath),
                    null,
                    "low",
                    true,
                    "Generate overload-specific S1InteropMember declarations for Harmony AccessTools.Method targets."));

                foreach (string sourceFile in automaticHarmonyOverloadRisks
                             .Select(risk => risk.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_harmony_overload_bindings",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple AccessTools.Method overload bindings to generated S1InteropMemberRegistry MethodInfo properties."));
                }
            }

            if (hasGeneratedMemberAccessTargets || hasAutomaticMemberAccessRewrites)
            {
                if (!operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"))
                {
                    operations.Add(new MigrationOperation(
                        "install_s1interop_generator_package",
                        project.ProjectPath,
                        null,
                        "low",
                        true,
                        "Install the S1Interop Roslyn generator package required by generated member access helpers."));
                }

                if (hasGeneratedMemberAccessTargets)
                {
                    operations.Add(new MigrationOperation(
                        "generate_member_access_targets",
                        MemberAccessTargetGenerator.GetSourcePath(project.ProjectPath),
                        null,
                        "low",
                        true,
                        "Generate S1InteropMember declarations for typed field/property reflection targets."));
                }

                foreach (string sourceFile in automaticMemberAccessRisks
                             .Select(risk => risk.FilePath)
                             .Concat(generatedMemberAccessTargets.Select(target => target.SourceFilePath))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_member_access_fallbacks",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple typed field/property reflection fallback getters through generated S1InteropMemberRegistry accessors."));
                }

                foreach (string sourceFile in automaticDirectMemberLookupRisks
                             .Select(risk => risk.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_direct_member_reflection_lookups",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple direct typeof(...).GetField/GetProperty lookups through generated S1InteropMemberRegistry FieldInfo/PropertyInfo accessors."));
                }
            }

            if (automaticObjectCastRisks.Length > 0)
            {
                if (!operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"))
                {
                    operations.Add(new MigrationOperation(
                        "install_s1interop_generator_package",
                        project.ProjectPath,
                        null,
                        "low",
                        true,
                        "Install the S1Interop Roslyn generator package required by generated backend-neutral object cast helpers."));
                }

                foreach (string sourceFile in automaticObjectCastRisks
                             .Select(risk => risk.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(new MigrationOperation(
                        "rewrite_il2cpp_object_casts",
                        sourceFile,
                        null,
                        "low",
                        true,
                        "Rewrite simple IL2CPP-prone C# pattern casts through the generated S1InteropObjectCast helper."));
                }
            }

            if (manualRisks.Length > 0)
            {
                operations.Add(new MigrationOperation(
                    "generate_source_risk_report",
                    SourceRiskReportGenerator.GetReportPath(project.ProjectPath),
                    null,
                    "low",
                    true,
                    "Generate a grouped IL2CPP source-risk report for migration cases that need a deliberate helper, patch, or runtime-specific rewrite."));
            }

            foreach (SourceRisk risk in manualRisks)
            {
                operations.Add(new MigrationOperation(
                    $"source_risk_{ToSnakeCase(risk.Kind)}",
                    risk.FilePath,
                    null,
                    risk.Risk,
                    Automatic: false,
                    $"{risk.Message} {risk.Remediation}",
                    risk.Evidence));
            }
        }

        return operations
            .DistinctBy(operation => (operation.RuleId, operation.FilePath, operation.Configuration, operation.Description))
            .ToArray();
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < value.Length; index++)
        {
            char ch = value[index];
            if (index > 0 && char.IsUpper(ch))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static void AddGeneratedGuardRuntimeDefineOperations(
        ProjectAnalysis project,
        List<MigrationOperation> operations)
    {
        foreach (ConfigurationAnalysis configuration in project.Configurations
                     .Where(configuration => configuration.Runtime == RuntimeKind.Mono))
        {
            if (configuration.DefineConstants.Any(define => string.Equals(define, "MONO", StringComparison.Ordinal)))
            {
                continue;
            }

            operations.Add(new MigrationOperation(
                "missing_runtime_define",
                project.ProjectPath,
                configuration.Name,
                "low",
                true,
                "Add MONO because dual-runtime source rewriting emits MONO guards for ScheduleOne namespace compatibility.",
                $"generated ScheduleOne using guards require MONO; DefineConstants={string.Join(";", configuration.DefineConstants)}"));
        }
    }

    private static bool ProjectHasIl2CppConfiguration(ProjectAnalysis project) =>
        project.Configurations.Any(configuration =>
            configuration.Runtime == RuntimeKind.Il2Cpp ||
            configuration.Name.Contains("il2cpp", StringComparison.OrdinalIgnoreCase));

    private static bool ProjectHasUnconditionedImportedMonoRuntimeFlag(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return false;
        }

        foreach (XElement import in document.Root!.Elements().Where(element => element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
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

            try
            {
                XDocument importedDocument = XDocument.Load(importedPath, LoadOptions.PreserveWhitespace);
                if (importedDocument.Descendants().Any(element =>
                        element.Name.LocalName.Equals("IsMono", StringComparison.OrdinalIgnoreCase) &&
                        element.Attribute("Condition") is null &&
                        element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                continue;
            }
        }

        return false;
    }

    private static bool ProjectHasStaleMonoReferenceImport(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return false;
        }

        return document.Root!.Elements()
            .Where(element => element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase))
            .Any(import =>
            {
                string project = import.Attribute("Project")?.Value ?? string.Empty;
                string condition = import.Attribute("Condition")?.Value ?? string.Empty;
                return project.Contains("Mono", StringComparison.OrdinalIgnoreCase) &&
                       !project.Contains("$(", StringComparison.Ordinal) &&
                       !condition.Contains("Il2cpp", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static MigrationOperation CreateTargetFrameworkOperation(ProjectAnalysis project, InteropDiagnostic diagnostic)
    {
        ConfigurationAnalysis? configuration = project.Configurations.FirstOrDefault(configuration =>
            string.Equals(configuration.Name, diagnostic.Configuration, StringComparison.OrdinalIgnoreCase));
        string? evidence = configuration is null
            ? diagnostic.Evidence
            : $"Runtime={configuration.Runtime}; {diagnostic.Evidence}";
        return new MigrationOperation(
            diagnostic.RuleId,
            project.ProjectPath,
            diagnostic.Configuration,
            "low",
            true,
            "Add or update a configuration-specific TargetFramework so IL2CPP builds use net6.0 and Mono builds use netstandard2.1.",
            evidence);
    }

    private static MigrationOperation? CreateHideFromIl2CppOperation(InteropDiagnostic diagnostic)
    {
        if (diagnostic.Evidence is null)
        {
            return null;
        }

        Match match = SourceDiagnosticEvidenceRegex.Match(diagnostic.Evidence);
        if (!match.Success)
        {
            return null;
        }

        string member = match.Groups["member"].Value;
        return new MigrationOperation(
            diagnostic.RuleId,
            match.Groups["path"].Value,
            null,
            "low",
            true,
            $"Add HideFromIl2Cpp to {member} so project-local helper types do not become part of the generated IL2CPP surface.",
            diagnostic.Evidence);
    }

    private static string GetDiagnosticFilePath(string evidence, string fallbackPath)
    {
        int lineMarker = evidence.IndexOf(".cs:", StringComparison.OrdinalIgnoreCase);
        if (lineMarker < 0)
        {
            return fallbackPath;
        }

        return evidence[..(lineMarker + ".cs".Length)];
    }
}
